using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace ComplexNutrition.Core
{
    /// <summary>One gut pool (net carbohydrate, protein or fats), from config/digestion.json.</summary>
    public class DigestionPool
    {
        /// <summary>Most the pool holds, in g. Food past a full pool passes through unused.</summary>
        [JsonProperty("maxGrams")] public float MaxGrams = 100;

        /// <summary>Each bite is absorbed evenly over this many in-game hours.</summary>
        [JsonProperty("absorbHours")] public float AbsorbHours = 6;

        /// <summary>Calories per gram absorbed.</summary>
        [JsonProperty("kcalPerGram")] public float KcalPerGram = 4;

        /// <summary>Grams absorbed within <see cref="WindowHours"/> above which each extra gram yields fewer calories.</summary>
        [JsonProperty("diminishingAboveGrams")] public float DiminishingAboveGrams = 100;

        /// <summary>The rolling window for <see cref="DiminishingAboveGrams"/>, in in-game hours (1 to 24).</summary>
        [JsonProperty("windowHours")] public int WindowHours = 24;

        /// <summary>How fast the yield falls above the line: an extra x grams count as scale * ln(1 + x / scale).</summary>
        [JsonProperty("diminishingScaleGrams")] public float DiminishingScaleGrams = 50;

        /// <summary>How much faster this pool digests with a full fibre reserve (0.3 = 30% faster).</summary>
        [JsonProperty("fiberSpeedBonus")] public float FiberSpeedBonus;

        /// <summary>Grams that count as calories after diminishing returns, for <paramref name="grams"/> absorbed on top of <paramref name="window"/>.</summary>
        public float Effective(float window, float grams)
        {
            return Curve(window + grams) - Curve(window);
        }

        private float Curve(float total)
        {
            if (total <= DiminishingAboveGrams) return total;
            float scale = Math.Max(1f, DiminishingScaleGrams);
            return DiminishingAboveGrams + scale * (float)Math.Log(1.0 + (total - DiminishingAboveGrams) / scale);
        }
    }

    /// <summary>
    /// The digestion rules (v0.2.2), from config/digestion.json: fullness from each food's weight,
    /// three gut pools that turn net carbohydrate, protein and fats into calories over time, and the
    /// calorie tank's starvation rule. Used while the tracking module runs.
    /// </summary>
    public class DigestionSettings
    {
        public const string AssetPath = "config/digestion.json";

        /// <summary>The fullness (vanilla satiety) maximum while tracking runs.</summary>
        [JsonProperty("fullnessMax")] public float FullnessMax = 300;

        /// <summary>Vanilla's own maximum, restored when tracking is switched off.</summary>
        [JsonProperty("vanillaFullnessMax")] public float VanillaFullnessMax = 1500;

        [JsonProperty("fullnessPerGram")] public float FullnessPerGram = 0.3f;

        /// <summary>Fullness lost per in-game hour, whatever the player does.</summary>
        [JsonProperty("fullnessDrainPerHour")] public float FullnessDrainPerHour = 50;

        /// <summary>Fullness multiplier for meals, pies and anything cooked, baked, smoked or cured.</summary>
        [JsonProperty("cookedFullness")] public float CookedFullness = 0.8f;

        /// <summary>Fullness multiplier for drinks and other liquids.</summary>
        [JsonProperty("liquidFullness")] public float LiquidFullness = 0.5f;

        /// <summary>Grain in porridge also counts its cooking water: this many times its own weight.</summary>
        [JsonProperty("porridgeWater")] public float PorridgeWater = 3f;

        /// <summary>Words in an item code that mark it as cooked for <see cref="CookedFullness"/>.</summary>
        [JsonProperty("cookedWords")] public string[] CookedWords = { "cooked", "smoked", "cured", "bread", "baked", "roasted", "charred" };

        /// <summary>Fullness per point of satiety handed out by other mods' code for food we cannot identify.</summary>
        [JsonProperty("unknownFullnessPerSatiety")] public float UnknownFullnessPerSatiety = 0.2f;

        /// <summary>Fibre reserve (in % of a day's need) at which the fibre speed-up is at its full size.</summary>
        [JsonProperty("fiberBonusFullAt")] public float FiberBonusFullAt = 70;

        /// <summary>Gut pools by nutrient code: carbohydrate (net of fibre), protein and fat.</summary>
        [JsonProperty("pools")] public Dictionary<string, DigestionPool> Pools = new();

        /// <summary>Most batches kept per pool; past this the two oldest are merged.</summary>
        [JsonProperty("maxBatches")] public int MaxBatches = 24;

        /// <summary>Protein absorbed over the last 24 hours is measured against this many grams.</summary>
        [JsonProperty("proteinNeedGrams")] public float ProteinNeedGrams = 128;

        /// <summary>Damage dealt every <see cref="StarvationSeconds"/> while the calorie tank is empty (vanilla: 0.125 HP every 6 s).</summary>
        [JsonProperty("starvationDamage")] public float StarvationDamage = 0.125f;

        [JsonProperty("starvationSeconds")] public float StarvationSeconds = 6f;

        /// <summary>After death, the calorie tank holds at least this much, in % of a day's need.</summary>
        [JsonProperty("reviveCalories")] public float ReviveCalories = 100;

        /// <summary>Below this share of a full tank the HUD calorie bar flashes.</summary>
        [JsonProperty("flashBelow")] public float FlashBelow = 0.1f;

        public static DigestionSettings Load(ICoreAPI api)
        {
            var asset = api.Assets.TryGet(new AssetLocation(ComplexNutritionModSystem.ModId, AssetPath));
            DigestionSettings s = null;
            try
            {
                s = asset?.ToObject<DigestionSettings>();
            }
            catch (Exception e)
            {
                api.Logger.Error("[complexnutrition] Could not read {0}, using defaults: {1}", AssetPath, e.Message);
            }
            s ??= new DigestionSettings();
            if (s.Pools == null || s.Pools.Count == 0)
            {
                s.Pools = new Dictionary<string, DigestionPool>
                {
                    [NutrientCodes.Carbohydrate] = new() { MaxGrams = 500, AbsorbHours = 3, KcalPerGram = 4, DiminishingAboveGrams = 300, WindowHours = 12, DiminishingScaleGrams = 150, FiberSpeedBonus = 0.1f },
                    [NutrientCodes.Protein] = new() { MaxGrams = 200, AbsorbHours = 6, KcalPerGram = 4, DiminishingAboveGrams = 130, WindowHours = 24, DiminishingScaleGrams = 65, FiberSpeedBonus = 0.2f },
                    [NutrientCodes.Fat] = new() { MaxGrams = 100, AbsorbHours = 24, KcalPerGram = 9, DiminishingAboveGrams = 70, WindowHours = 24, DiminishingScaleGrams = 35, FiberSpeedBonus = 0.3f },
                };
            }
            return s;
        }

        public DigestionPool Pool(string code) => code != null && Pools.TryGetValue(code, out var p) ? p : null;

        /// <summary>Grams a food adds to a pool: net carbohydrate (carbohydrate minus fibre) for carbs, the plain amount otherwise.</summary>
        public static float PoolGrams(string pool, System.Func<string, float> amount)
        {
            if (pool == NutrientCodes.Carbohydrate) return Math.Max(0f, amount(NutrientCodes.Carbohydrate) - amount(NutrientCodes.Fiber));
            return Math.Max(0f, amount(pool));
        }

        /// <summary>Calories the body gets from these amounts at the pools' rates, before diminishing returns.</summary>
        public float KcalFromMacros(System.Func<string, float> amount)
        {
            float kcal = 0f;
            foreach (var kv in Pools) kcal += PoolGrams(kv.Key, amount) * kv.Value.KcalPerGram;
            return kcal;
        }

        public bool IsCookedCode(AssetLocation code)
        {
            string path = code?.Path;
            if (path == null || CookedWords == null) return false;
            foreach (var w in CookedWords)
            {
                if (!string.IsNullOrEmpty(w) && path.Contains(w)) return true;
            }
            return false;
        }
    }

    /// <summary>Fullness worked out from food weight, and the switch between our fullness scale and vanilla's.</summary>
    public static class Digestion
    {
        /// <summary>The rules in force, or null while tracking does not run. Set by the tracking module.</summary>
        public static DigestionSettings Settings { get; set; }

        /// <summary>True when our fullness rules apply to this entity: tracking runs and has picked the player up.</summary>
        public static bool IsActiveFor(Entity entity)
        {
            if (Settings == null || entity is not EntityPlayer) return false;
            var tree = entity.WatchedAttributes.GetTreeAttribute(NutritionState.TreeKey);
            return tree != null && tree.GetBool("tracking");
        }

        /// <summary>Fullness of one portion of a single food (one item, or one litre of a liquid).</summary>
        public static float FoodFullness(DigestionSettings s, ResolvedFoodProfile food, CollectibleObject coll)
        {
            if (s == null || food == null) return 0f;
            float f = food.PortionGrams * s.FullnessPerGram;
            if (food.IsLiquid) f *= s.LiquidFullness;
            if (s.IsCookedCode(coll?.Code)) f *= s.CookedFullness;
            return f;
        }

        /// <summary>
        /// Fullness of a dish's contents as the container stores them: one serving for pots, bowls and crocks,
        /// the whole pie for pies. Spoilage counts, the way vanilla counts it for satiety, when
        /// <paramref name="forEntity"/> or <paramref name="inSlot"/> is given.
        /// </summary>
        public static float MealFullness(ComplexNutritionModSystem core, IWorldAccessor world, ItemSlot inSlot, ItemStack dish, ItemStack[] contents, EntityAgent forEntity, bool withSpoilage)
        {
            var s = Settings;
            if (s == null || core == null || contents == null) return 0f;

            string recipe = dish?.Attributes?.GetString("recipeCode");
            bool isPie = dish?.Collectible is BlockPie;
            bool cookedDish = isPie || !string.IsNullOrEmpty(recipe);
            bool porridge = recipe != null && recipe.Contains("porridge");
            bool frozen = dish?.Attributes?.GetBool("timeFrozen") == true;

            float total = 0f;
            foreach (var stack in contents)
            {
                if (stack?.Collectible == null) continue;
                var food = core.Foods.Resolve(stack.Collectible);
                float f;
                if (food == null)
                {
                    // An ingredient we have no data for: scale its vanilla in-meal satiety.
                    var v = FoodDatabase.ReadVanilla(stack.Collectible);
                    f = v.Satiety * stack.StackSize * s.UnknownFullnessPerSatiety;
                }
                else
                {
                    float grams;
                    if (food.IsLiquid)
                    {
                        var props = BlockLiquidContainerBase.GetContainableProps(stack);
                        grams = stack.StackSize / (props?.ItemsPerLitre ?? 100f) * food.PortionGrams;
                    }
                    else
                    {
                        grams = stack.StackSize * food.PortionGrams;
                    }
                    if (porridge && FoodDatabase.ReadVanilla(stack.Collectible).Category == "Grain") grams *= 1f + s.PorridgeWater;
                    f = grams * s.FullnessPerGram;
                    if (food.IsLiquid) f *= s.LiquidFullness;
                    if (!cookedDish && s.IsCookedCode(stack.Collectible.Code)) f *= s.CookedFullness;
                }

                if (withSpoilage && !frozen && world != null)
                {
                    var slot = new DummySlot(stack, inSlot?.Inventory);
                    float level = stack.Collectible.UpdateAndGetTransitionState(world, slot, EnumTransitionType.Perish)?.TransitionLevel ?? 0f;
                    f *= GlobalConstants.FoodSpoilageSatLossMul(level, dish, forEntity);
                }
                total += f;
            }

            if (cookedDish) total *= s.CookedFullness;
            if (isPie && withSpoilage && inSlot != null && dish.Collectible is BlockPie pie)
            {
                total *= pie.GetNutritionHealthMul(null, inSlot, forEntity)[1];
            }
            return total;
        }

        private const string ScaledKey = "fullnessScaled";

        /// <summary>Puts a player on our fullness scale (max 300), scaling what is in the stomach to match. Safe to call again.</summary>
        public static void UseOurScale(Entity entity, DigestionSettings s)
        {
            var hunger = entity?.GetBehavior<EntityBehaviorHunger>();
            var tree = entity?.WatchedAttributes.GetTreeAttribute(NutritionState.TreeKey);
            if (hunger == null || tree == null || s == null) return;
            float max = hunger.MaxSaturation;
            if (Math.Abs(max - s.FullnessMax) > 0.01f && max > 0f)
            {
                float cur = hunger.Saturation * s.FullnessMax / max;
                hunger.MaxSaturation = s.FullnessMax;
                hunger.Saturation = Math.Min(s.FullnessMax, cur);
            }
            tree.SetBool(ScaledKey, true);
        }

        /// <summary>Puts a player back on vanilla's scale if we changed it. Safe to call when we did not.</summary>
        public static void UseVanillaScale(Entity entity, float vanillaMax)
        {
            var hunger = entity?.GetBehavior<EntityBehaviorHunger>();
            var tree = entity?.WatchedAttributes.GetTreeAttribute(NutritionState.TreeKey);
            if (hunger == null || tree == null || !tree.GetBool(ScaledKey)) return;
            float max = hunger.MaxSaturation;
            if (max > 0f && Math.Abs(max - vanillaMax) > 0.01f)
            {
                float cur = hunger.Saturation * vanillaMax / max;
                hunger.MaxSaturation = vanillaMax;
                hunger.Saturation = Math.Min(vanillaMax, cur);
            }
            tree.SetBool(ScaledKey, false);
            entity.WatchedAttributes.MarkPathDirty(NutritionState.TreeKey);
        }
    }
}
