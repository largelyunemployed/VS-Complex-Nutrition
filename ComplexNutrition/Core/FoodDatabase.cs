using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace ComplexNutrition.Core
{
    /// <summary>A profile matched to one collectible, with the portion size worked out.</summary>
    public class ResolvedFoodProfile
    {
        public FoodProfile Profile;

        /// <summary>True when the food had no profile and got its vanilla category's template.</summary>
        public bool IsFallback;

        public float PortionGrams;

        public bool IsLiquid => Profile.IsLiquid;

        public float PerPortion(string nutrient) => Profile.Per100(nutrient) * PortionGrams / 100f;
    }

    /// <summary>Vanilla nutrition values for a collectible, read from wherever the game keeps them.</summary>
    public struct VanillaNutrition
    {
        public float Satiety;
        public string Category;
        /// <summary>"eaten", "inmeal" or "perlitre".</summary>
        public string Kind;

        public bool IsFood => Kind != null;
    }

    /// <summary>
    /// Holds every food profile and answers "what is in this item?".
    /// Profiles are JSON assets under config/foodprofiles/ in any domain, so other mods
    /// can add their own files or patch ours.
    /// </summary>
    public class FoodDatabase
    {
        public const string ProfileFolder = "config/foodprofiles/";
        public const string FallbackPath = "config/foodfallbacks.json";

        private readonly Dictionary<string, FoodProfile> exact = new();
        private readonly List<(AssetLocation pattern, FoodProfile profile)> wildcards = new();
        private readonly Dictionary<int, ResolvedFoodProfile> itemCache = new();
        private readonly Dictionary<int, ResolvedFoodProfile> blockCache = new();

        public FoodFallbackFile Fallbacks { get; private set; } = new();
        public int ProfileCount => exact.Count + wildcards.Count;

        public void Load(ICoreAPI api, NutrientRegistry registry)
        {
            exact.Clear();
            wildcards.Clear();
            itemCache.Clear();
            blockCache.Clear();

            foreach (var asset in api.Assets.GetManyInCategory("config", "foodprofiles/", null, true))
            {
                if (!asset.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                FoodProfileFile file;
                try
                {
                    file = asset.ToObject<FoodProfileFile>();
                }
                catch (Exception e)
                {
                    api.Logger.Error("[complexnutrition] Could not read {0}: {1}", asset.Location, e.Message);
                    continue;
                }

                foreach (var p in file.Profiles)
                {
                    if (string.IsNullOrWhiteSpace(p.Code)) continue;
                    WarnUnknownNutrients(api, registry, p, asset.Location.ToString());
                    var loc = new AssetLocation(p.Code);
                    if (loc.Path.Contains('*'))
                    {
                        wildcards.Add((loc, p));
                    }
                    else if (!exact.TryAdd(loc.ToString(), p))
                    {
                        api.Logger.Warning("[complexnutrition] {0} has a second profile in {1}; keeping the first.", loc, asset.Location);
                    }
                }
            }

            var fallbackAsset = api.Assets.TryGet(new AssetLocation(ComplexNutritionModSystem.ModId, FallbackPath));
            Fallbacks = fallbackAsset?.ToObject<FoodFallbackFile>() ?? new FoodFallbackFile();
            foreach (var kv in Fallbacks.ByCategory) WarnUnknownNutrients(api, registry, kv.Value, FallbackPath);
        }

        private static void WarnUnknownNutrients(ICoreAPI api, NutrientRegistry registry, FoodProfile p, string where)
        {
            if (p.Per100g == null) return;
            foreach (var key in p.Per100g.Keys)
            {
                if (!registry.Contains(key))
                {
                    api.Logger.Warning("[complexnutrition] {0}: profile {1} uses unknown nutrient '{2}'.", where, p.Code, key);
                }
            }
        }

        /// <summary>Finds the profile written for this exact code or a wildcard, ignoring fallbacks.</summary>
        public FoodProfile FindOwnProfile(AssetLocation code)
        {
            if (code == null) return null;
            if (exact.TryGetValue(code.ToString(), out var p)) return p;
            foreach (var (pattern, profile) in wildcards)
            {
                if (WildcardUtil.Match(pattern, code)) return profile;
            }
            return null;
        }

        /// <summary>
        /// Returns the profile for a collectible, a category fallback for unlisted foods,
        /// or null for things that are not food. Meals, pies and other composite foods
        /// return null: their nutrition will be summed from ingredients when eaten (v0.2).
        /// </summary>
        public ResolvedFoodProfile Resolve(CollectibleObject coll)
        {
            if (coll?.Code == null) return null;
            var cache = coll.ItemClass == EnumItemClass.Block ? blockCache : itemCache;
            if (cache.TryGetValue(coll.Id, out var cached)) return cached;

            ResolvedFoodProfile result = null;
            var vanilla = ReadVanilla(coll);
            if (vanilla.IsFood && !IsComposite(coll))
            {
                // Only things vanilla treats as food get data, so a broad wildcard such as
                // "game:mushroom-*" never puts nutrients on a decorative or non-edible variant.
                var own = FindOwnProfile(coll.Code);
                result = own != null
                    ? new ResolvedFoodProfile { Profile = own, PortionGrams = own.PortionGrams }
                    : MakeFallback(vanilla);
            }

            cache[coll.Id] = result;
            return result;
        }

        private ResolvedFoodProfile MakeFallback(VanillaNutrition vanilla)
        {
            if (!Fallbacks.ByCategory.TryGetValue(vanilla.Category ?? "Unknown", out var template)
                && !Fallbacks.ByCategory.TryGetValue("Unknown", out template))
            {
                return null;
            }

            float kcalPer100 = template.Per100(NutrientCodes.Calories);
            if (kcalPer100 <= 0 || vanilla.Satiety <= 0) return null;

            float grams = vanilla.Satiety * Fallbacks.KcalPerSatiety / kcalPer100 * 100f;
            var profile = new FoodProfile
            {
                Code = template.Code,
                PortionGrams = grams,
                PortionUnit = vanilla.Kind == "perlitre" ? "litre" : "item",
                Per100g = template.Per100g,
                Source = template.Source,
                Notes = template.Notes
            };
            return new ResolvedFoodProfile { Profile = profile, PortionGrams = grams, IsFallback = true };
        }

        /// <summary>Meals, pies, sandwiches and similar are made of ingredients and are not profiled directly.</summary>
        public static bool IsComposite(CollectibleObject coll)
        {
            string cls = coll.Class ?? "";
            return cls.Contains("Meal") || cls.Contains("Pie") || cls.Contains("Sandwich")
                || cls.Contains("CookedContainer") || cls.Contains("Crock");
        }

        /// <summary>Reads satiety and category from the three places vanilla keeps them.</summary>
        public static VanillaNutrition ReadVanilla(CollectibleObject coll)
        {
            if (coll.NutritionProps != null)
            {
                return new VanillaNutrition
                {
                    Satiety = coll.NutritionProps.Satiety,
                    Category = coll.NutritionProps.FoodCategory.ToString(),
                    Kind = "eaten"
                };
            }

            JsonObject attrs = coll.Attributes;
            if (attrs == null) return default;

            var inMeal = attrs["nutritionPropsWhenInMeal"];
            if (inMeal.Exists)
            {
                return new VanillaNutrition
                {
                    Satiety = inMeal["satiety"].AsFloat(0),
                    Category = inMeal["foodcategory"].AsString("Unknown"),
                    Kind = "inmeal"
                };
            }

            var perLitre = attrs["waterTightContainerProps"]["nutritionPropsPerLitre"];
            if (perLitre.Exists)
            {
                return new VanillaNutrition
                {
                    Satiety = perLitre["satiety"].AsFloat(0),
                    Category = perLitre["foodcategory"].AsString("Unknown"),
                    Kind = "perlitre"
                };
            }

            return default;
        }

        /// <summary>Every loaded collectible that vanilla treats as food, with how it is covered.</summary>
        public IEnumerable<(CollectibleObject coll, VanillaNutrition vanilla, ResolvedFoodProfile resolved)> Survey(IWorldAccessor world)
        {
            foreach (var coll in world.Collectibles.Where(c => c?.Code != null))
            {
                var vanilla = ReadVanilla(coll);
                if (!vanilla.IsFood) continue;
                yield return (coll, vanilla, Resolve(coll));
            }
        }
    }
}
