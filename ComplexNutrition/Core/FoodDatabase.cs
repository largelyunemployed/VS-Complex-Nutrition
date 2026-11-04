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
    /// can add their own files (see docs/COMPATIBILITY.md), override ours by priority, or patch ours.
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

            // Profiles may come from any mod: every config/foodprofiles/*.json in every asset domain is read.
            var loaded = new List<(FoodProfile profile, string file)>();
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
                if (file?.Profiles == null) continue;
                foreach (var p in file.Profiles)
                {
                    if (p == null || string.IsNullOrWhiteSpace(p.Code)) continue;
                    loaded.Add((p, asset.Location.ToString()));
                }
            }

            foreach (var (p, fileName) in loaded) ApplyDailyValues(api, registry, p, fileName);

            // Higher priority first; equal priorities keep load order and the first one wins.
            var winners = new Dictionary<string, (int priority, string file)>();
            foreach (var (p, fileName) in loaded.OrderByDescending(x => x.profile.Priority))
            {
                var loc = new AssetLocation(p.Code);
                string key = loc.ToString();
                if (winners.TryGetValue(key, out var won))
                {
                    if (won.priority == p.Priority)
                        api.Logger.Warning("[complexnutrition] {0} has profiles in both {1} and {2} at priority {3}; keeping {1}. Set \"priority\" to choose.", key, won.file, fileName, p.Priority);
                    continue;
                }
                winners[key] = (p.Priority, fileName);
                if (loc.Path.Contains('*')) wildcards.Add((loc, p));
                else exact[key] = p;
            }

            foreach (var (p, fileName) in loaded)
            {
                if (winners.TryGetValue(new AssetLocation(p.Code).ToString(), out var w) && w.file == fileName)
                {
                    ResolveLike(api, p, fileName, new HashSet<FoodProfile>());
                    WarnUnknownNutrients(api, registry, p, fileName);
                }
            }

            // Drop profiles that ended up unusable, so those foods use the category estimate instead.
            foreach (var key in exact.Where(kv => !IsUsable(kv.Value)).Select(kv => kv.Key).ToList())
            {
                api.Logger.Warning("[complexnutrition] Profile {0} has no portionGrams or no nutrients; using the category estimate instead.", key);
                exact.Remove(key);
            }
            wildcards.RemoveAll(w => !IsUsable(w.profile));

            Fallbacks = ConfigAsset.Load<FoodFallbackFile>(api, FallbackPath);
            Fallbacks.ByCategory ??= new Dictionary<string, FoodProfile>();
            foreach (var kv in Fallbacks.ByCategory) WarnUnknownNutrients(api, registry, kv.Value, FallbackPath);
        }

        /// <summary>Turns a profile's per100gDV (% of the daily value) into per100g amounts in each nutrient's unit.</summary>
        public static void ApplyDailyValues(ICoreAPI api, NutrientRegistry registry, FoodProfile p, string fileName)
        {
            if (p.Per100gDV == null) return;
            p.Per100g ??= new Dictionary<string, float>();
            foreach (var kv in p.Per100gDV)
            {
                var def = registry.Get(kv.Key);
                if (def == null)
                {
                    api?.Logger.Warning("[complexnutrition] {0}: profile {1} uses unknown nutrient '{2}' in per100gDV.", fileName, p.Code, kv.Key);
                    continue;
                }
                if (p.Per100g.ContainsKey(kv.Key))
                {
                    api?.Logger.Warning("[complexnutrition] {0}: profile {1} gives {2} in both per100g and per100gDV; using per100g.", fileName, p.Code, kv.Key);
                    continue;
                }
                p.Per100g[kv.Key] = def.FromPercent(kv.Value);
            }
            p.Per100gDV = null;
        }

        private static bool IsUsable(FoodProfile p) => p.PortionGrams > 0 && p.Per100g != null && p.Per100g.Count > 0;

        /// <summary>Fills a profile from the food named in its "like" field, then applies "scale".</summary>
        private void ResolveLike(ICoreAPI api, FoodProfile p, string fileName, HashSet<FoodProfile> visiting)
        {
            if (!visiting.Add(p)) return;
            if (!string.IsNullOrWhiteSpace(p.Like))
            {
                var baseProfile = FindOwnProfile(new AssetLocation(p.Like));
                if (baseProfile == null || baseProfile == p)
                {
                    api.Logger.Warning("[complexnutrition] {0}: profile {1} is \"like\" {2}, which has no profile.", fileName, p.Code, p.Like);
                }
                else
                {
                    ResolveLike(api, baseProfile, fileName, visiting);   // in case the base is itself "like" something
                    var merged = new Dictionary<string, float>(baseProfile.Per100g ?? new Dictionary<string, float>());
                    if (p.Per100g != null) foreach (var kv in p.Per100g) merged[kv.Key] = kv.Value;
                    p.Per100g = merged;
                    if (p.PortionGrams <= 0) p.PortionGrams = baseProfile.PortionGrams;
                    p.PortionUnit ??= baseProfile.PortionUnit;
                    p.Source ??= baseProfile.Source;
                    p.Notes ??= "Based on " + p.Like + ".";
                }
                p.Like = null;   // resolved once
            }
            if (p.Scale != null && p.Per100g != null)
            {
                p.Scale.TryGetValue("all", out var all);
                foreach (var key in p.Per100g.Keys.ToList())
                {
                    float f = p.Scale.TryGetValue(key, out var s) ? s : 1f;
                    if (all > 0) f *= all;
                    p.Per100g[key] *= f;
                }
                p.Scale = null;
            }
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
                    : FallbackFor(vanilla.Category, vanilla.Satiety, vanilla.Kind == "perlitre");
            }

            cache[coll.Id] = result;
            return result;
        }

        /// <summary>
        /// The category template sized so that one portion gives <paramref name="satiety"/> worth of calories.
        /// Used for foods without a profile, and for satiety another mod hands out directly.
        /// </summary>
        public ResolvedFoodProfile FallbackFor(string category, float satiety, bool liquid)
        {
            if (!Fallbacks.ByCategory.TryGetValue(category ?? "Unknown", out var template)
                && !Fallbacks.ByCategory.TryGetValue("Unknown", out template))
            {
                return null;
            }

            float kcalPer100 = template.Per100(NutrientCodes.Calories);
            if (kcalPer100 <= 0 || satiety <= 0) return null;

            float grams = satiety * Fallbacks.KcalPerSatiety / kcalPer100 * 100f;
            var profile = new FoodProfile
            {
                Code = template.Code,
                PortionGrams = grams,
                PortionUnit = liquid ? "litre" : "item",
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
