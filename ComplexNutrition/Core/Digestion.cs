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

        /// <summary>Fastest absorption, in g per in-game hour, reached when the pool is well filled.</summary>
        [JsonProperty("maxRateGrams")] public float MaxRateGrams = 10;

        /// <summary>Contents, in g, at which the pool absorbs at half its fastest rate. Below this it slows down sharply.</summary>
        [JsonProperty("halfRateGrams")] public float HalfRateGrams = 10;

        /// <summary>Below this many grams a pool counts as empty and the rest is absorbed at once.</summary>
        public const float EmptyGrams = 0.5f;

        /// <summary>
        /// Absorption rate in g per hour for <paramref name="grams"/> in the pool: it rises with the contents and levels
        /// off at <see cref="MaxRateGrams"/> (times <paramref name="speed"/>, the fibre bonus), like a real gut, which
        /// absorbs little when nearly empty and has a limit when full.
        /// </summary>
        public float Rate(float grams, float speed) =>
            grams <= 0f ? 0f : Math.Max(0.01f, MaxRateGrams) * speed * grams / (Math.Max(0.01f, HalfRateGrams) + grams);

        /// <summary>Grams left in the pool after <paramref name="hours"/> of digestion from <paramref name="grams"/>.</summary>
        public float Absorb(float grams, float hours, float speed)
        {
            if (grams <= 0f || hours <= 0f) return Math.Max(0f, grams);
            int steps = Math.Clamp((int)Math.Ceiling(hours / 0.05f), 1, 2000);
            float dt = hours / steps;
            for (int i = 0; i < steps && grams > EmptyGrams; i++)
            {
                float mid = grams - Rate(grams, speed) * dt / 2f;   // midpoint step
                grams = Math.Max(0f, grams - Rate(Math.Max(0f, mid), speed) * dt);
            }
            return grams <= EmptyGrams ? 0f : grams;
        }

        /// <summary>In-game hours until a pool holding <paramref name="grams"/> is empty, at this fibre <paramref name="speed"/>.</summary>
        public float HoursToEmpty(float grams, float speed)
        {
            if (grams <= EmptyGrams) return 0f;
            float k = Math.Max(0.01f, HalfRateGrams);
            return (float)((grams - EmptyGrams + k * Math.Log(grams / EmptyGrams)) / (Math.Max(0.01f, MaxRateGrams) * speed));
        }

        /// <summary>Calories per gram absorbed.</summary>
        [JsonProperty("kcalPerGram")] public float KcalPerGram = 4;

        /// <summary>Grams absorbed within <see cref="WindowHours"/> above which each extra gram yields fewer calories.</summary>
        [JsonProperty("diminishingAboveGrams")] public float DiminishingAboveGrams = 100;

        /// <summary>The rolling window for <see cref="DiminishingAboveGrams"/>, in in-game hours (1 to 24).</summary>
        [JsonProperty("windowHours")] public int WindowHours = 24;

        /// <summary>How fast the yield falls above the line: an extra x grams count as scale * ln(1 + x / scale).</summary>
        [JsonProperty("diminishingScaleGrams")] public float DiminishingScaleGrams = 50;

        /// <summary>
        /// Optional two-tier taper (v0.3.0, protein). When set above <see cref="DiminishingAboveGrams"/>, the yield per gram falls
        /// in a straight line from full at the diminishing line to <see cref="TaperEndValue"/> here, and every gram past it
        /// yields <see cref="BeyondValue"/>. The log curve is not used. 0 = off.
        /// </summary>
        [JsonProperty("taperEndGrams")] public float TaperEndGrams;

        /// <summary>Yield per gram at <see cref="TaperEndGrams"/> (0.8 = 80%).</summary>
        [JsonProperty("taperEndValue")] public float TaperEndValue = 1f;

        /// <summary>Yield per gram past <see cref="TaperEndGrams"/> (0.5 = 50%).</summary>
        [JsonProperty("beyondValue")] public float BeyondValue = 1f;

        /// <summary>How much faster this pool digests with a full fibre reserve (0.3 = 30% faster).</summary>
        [JsonProperty("fiberSpeedBonus")] public float FiberSpeedBonus;

        /// <summary>Grams that count as calories after diminishing returns, for <paramref name="grams"/> absorbed on top of <paramref name="window"/>.</summary>
        public float Effective(float window, float grams)
        {
            if (!DiminishingReturns) return grams;
            return Curve(window + grams) - Curve(window);
        }

        /// <summary>
        /// The world's Diminishing returns setting (v0.3.4). Off, every gram counts in full. Kept in step with the world by the
        /// server and, on the client, by the settings the server sends.
        /// </summary>
        public static bool DiminishingReturns = true;

        private float Curve(float total)
        {
            if (total <= DiminishingAboveGrams) return total;
            if (TaperEndGrams > DiminishingAboveGrams)
            {
                float span = TaperEndGrams - DiminishingAboveGrams;
                float over = Math.Min(total, TaperEndGrams) - DiminishingAboveGrams;
                float drop = 1f - TaperEndValue;
                float v = DiminishingAboveGrams + over - drop * over * over / (2f * span);
                if (total > TaperEndGrams) v += (total - TaperEndGrams) * BeyondValue;
                return v;
            }
            float scale = Math.Max(1f, DiminishingScaleGrams);
            return DiminishingAboveGrams + scale * (float)Math.Log(1.0 + (total - DiminishingAboveGrams) / scale);
        }
    }

    /// <summary>
    /// The digestion rules (v0.2.2), from config/digestion.json: fullness from each food's weight,
    /// three gut pools that turn net carbohydrate, protein and fats into calories over time, and the
    /// calorie reserve's starvation rule. Used while the tracking module runs.
    /// </summary>
    public class DigestionSettings
    {
        public const string AssetPath = "config/digestion.json";

        /// <summary>The fullness (vanilla satiety) maximum while tracking runs.</summary>
        [JsonProperty("fullnessMax")] public float FullnessMax = 600;

        /// <summary>Vanilla's own maximum, restored when tracking is switched off.</summary>
        [JsonProperty("vanillaFullnessMax")] public float VanillaFullnessMax = 1500;

        [JsonProperty("fullnessPerGram")] public float FullnessPerGram = 0.3f;

        /// <summary>Fullness lost per in-game hour, whatever the player does.</summary>
        [JsonProperty("fullnessDrainPerHour")] public float FullnessDrainPerHour = 100;

        /// <summary>Fullness multiplier for meals, pies and anything cooked, baked, smoked or cured.</summary>
        [JsonProperty("cookedFullness")] public float CookedFullness = 0.8f;

        /// <summary>Fullness multiplier for drinks and other liquids.</summary>
        [JsonProperty("liquidFullness")] public float LiquidFullness = 0.5f;

        /// <summary>Words in an item code that mark it as cooked for <see cref="CookedFullness"/>.</summary>
        [JsonProperty("cookedWords")] public string[] CookedWords = { "cooked", "smoked", "cured", "bread", "baked", "roasted", "charred" };

        /// <summary>Fullness per point of satiety handed out by other mods' code for food we cannot identify.</summary>
        [JsonProperty("unknownFullnessPerSatiety")] public float UnknownFullnessPerSatiety = 0.2f;

        /// <summary>Fibre reserve (in % of a day's need) at which the fibre speed-up is at its full size.</summary>
        [JsonProperty("fiberBonusFullAt")] public float FiberBonusFullAt = 70;

        /// <summary>Gut pools by nutrient code: carbohydrate (net of fibre), protein and fat.</summary>
        [JsonProperty("pools")] public Dictionary<string, DigestionPool> Pools = new();


        /// <summary>
        /// Protein the body needs over any 24 hours, at least: the protein pool's diminishing line, so the number lives
        /// in one place. The protein bar's line marks it.
        /// </summary>
        [JsonIgnore] public float ProteinNeedGrams => Pool(NutrientCodes.Protein)?.DiminishingAboveGrams ?? 64f;

        /// <summary>The end of the protein pool's taper, past which each gram yields only its beyond value. The protein bar's top.</summary>
        [JsonIgnore] public float ProteinMaxGrams
        {
            get
            {
                var p = Pool(NutrientCodes.Protein);
                if (p == null) return 128f;
                return p.TaperEndGrams > p.DiminishingAboveGrams ? p.TaperEndGrams : p.DiminishingAboveGrams * 2f;
            }
        }

        /// <summary>Calories per point of vanilla satiety: vanilla's satiety burn costs this many kcal, and switching tracking off turns the reserve back into satiety at this rate.</summary>
        [JsonProperty("kcalPerSatiety")] public float KcalPerSatiety = 1f;

        /// <summary>Damage dealt every <see cref="StarvationSeconds"/> while the calorie reserve is empty (vanilla: 0.125 HP every 6 s).</summary>
        [JsonProperty("starvationDamage")] public float StarvationDamage = 0.125f;

        [JsonProperty("starvationSeconds")] public float StarvationSeconds = 6f;

        /// <summary>Below this share of a full reserve the HUD calorie bar flashes.</summary>
        [JsonProperty("flashBelow")] public float FlashBelow = 0.1f;

        public static DigestionSettings Load(ICoreAPI api)
        {
            var s = ConfigAsset.Load<DigestionSettings>(api, AssetPath);
            if (s.Pools == null || s.Pools.Count == 0) s.Pools = DefaultPools();
            return s;
        }

        /// <summary>The built-in gut pools, used when digestion.json has none. They match the shipped file (a test checks).</summary>
        public static Dictionary<string, DigestionPool> DefaultPools() => new()
        {
            [NutrientCodes.Carbohydrate] = new() { MaxGrams = 500, MaxRateGrams = 90, HalfRateGrams = 30, KcalPerGram = 4, DiminishingAboveGrams = 300, WindowHours = 12, DiminishingScaleGrams = 150, FiberSpeedBonus = 0.1f },
            [NutrientCodes.Protein] = new() { MaxGrams = 200, MaxRateGrams = 15, HalfRateGrams = 10, KcalPerGram = 4, DiminishingAboveGrams = 64, TaperEndGrams = 128, TaperEndValue = 0.8f, BeyondValue = 0.5f, WindowHours = 24, FiberSpeedBonus = 0.2f },
            [NutrientCodes.Fat] = new() { MaxGrams = 240, MaxRateGrams = 6, HalfRateGrams = 15, KcalPerGram = 9, DiminishingAboveGrams = 70, WindowHours = 24, DiminishingScaleGrams = 35, FiberSpeedBonus = 0.3f },
        };

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
            return NutritionState.Of(entity)?.TrackingOn == true;
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

        /// <summary>
        /// Whether this player sees the advanced nutrition display: their own choice (/cnutrition display) if they made
        /// one, otherwise the world setting.
        /// </summary>
        public static bool AdvancedDisplay(IWorldAccessor world, NutritionState state)
        {
            int own = state?.DisplayChoice ?? 0;
            if (own != 0) return own == 1;
            return WorldAdvancedDefault(world);
        }

        /// <summary>The world's Advanced nutrition display setting (set at world creation, or in the settings menu).</summary>
        public static bool WorldAdvancedDefault(IWorldAccessor world) => WorldSettings.Bool(world, WorldSettings.AdvancedDisplayKey);

        /// <summary>Digestion speed from the fibre reserve: 1 when empty, 1 + the pool's bonus at the full-bonus line or above.</summary>
        public static float FiberSpeed(DigestionSettings s, NutritionState state, DigestionPool pool)
        {
            float full = Math.Max(1f, s?.FiberBonusFullAt ?? 70f);
            float share = Math.Clamp(state.GetReserve(NutrientCodes.Fiber) / full, 0f, 1f);
            return 1f + pool.FiberSpeedBonus * share;
        }

        /// <summary>Grams of a pool absorbed within its diminishing-returns window, up to the last digestion step.</summary>
        /// <summary>
        /// The extra calories per in-game hour that fiber is adding right now: for each gut pool, what it yields at its
        /// current rate with the fiber speed-up, less what it would yield without, after diminishing returns.
        /// </summary>
        public static float FiberKcalPerHour(DigestionSettings s, NutritionState state, NutrientRegistry nutrients)
        {
            if (s == null || state == null || nutrients == null) return 0f;
            float kcal = 0f;
            foreach (var kv in s.Pools)
            {
                var def = nutrients.Get(kv.Key);
                var pool = kv.Value;
                if (def == null) continue;
                float grams = def.FromPercent(state.GetReserve(def.Code));
                if (grams <= DigestionPool.EmptyGrams) continue;
                float window = WindowGrams(state, kv.Key, pool);
                float fast = pool.Effective(window, pool.Rate(grams, FiberSpeed(s, state, pool)));
                float plain = pool.Effective(window, pool.Rate(grams, 1f));
                kcal += Math.Max(0f, fast - plain) * pool.KcalPerGram;
            }
            return kcal;
        }

        /// <summary>
        /// What the gut holds, in calories: about how many it will still give once everything is digested (each pool's
        /// contents after diminishing returns, against the totals so far), and how many of the food in it are lost to them.
        /// </summary>
        public static (float coming, float lost) GutOutlook(DigestionSettings s, NutritionState state, NutrientRegistry nutrients)
        {
            float coming = 0f, lost = 0f;
            if (s == null || state == null || nutrients == null) return (0f, 0f);
            foreach (var kv in s.Pools)
            {
                var def = nutrients.Get(kv.Key);
                var pool = kv.Value;
                if (def == null) continue;
                float grams = def.FromPercent(state.GetReserve(def.Code));
                if (grams <= DigestionPool.EmptyGrams) continue;
                float full = grams * pool.KcalPerGram;
                float got = pool.Effective(WindowGrams(state, kv.Key, pool), grams) * pool.KcalPerGram;
                coming += got;
                lost += Math.Max(0f, full - got);
            }
            return (coming, lost);
        }

        public static float WindowGrams(NutritionState state, string code, DigestionPool pool)
        {
            var log = state.GetAbsorbedLog(code);
            long hour = state.AbsorbedHour;
            if (log == null || hour == long.MinValue) return 0f;
            float window = 0f;
            int span = Math.Clamp(pool.WindowHours, 1, 24);
            for (int k = 0; k < span; k++) window += log[(int)(((hour - k) % 24 + 24) % 24)];
            return window;
        }

        private const string ScaledKey = "fullnessScaled";
        private const string SavedFullnessKey = "savedFullness";

        /// <summary>
        /// Puts a player on our fullness scale (max 600). A player coming back from vanilla gets back the fullness
        /// they had when tracking was switched off, whatever they ate since; anyone else has their stomach scaled
        /// to match. Safe to call again.
        /// </summary>
        public static void UseOurScale(Entity entity, DigestionSettings s)
        {
            var hunger = entity?.GetBehavior<EntityBehaviorHunger>();
            var tree = entity?.WatchedAttributes.GetTreeAttribute(NutritionState.TreeKey);
            if (hunger == null || tree == null || s == null) return;
            if (tree.HasAttribute(SavedFullnessKey))
            {
                hunger.MaxSaturation = s.FullnessMax;
                hunger.Saturation = Math.Clamp(tree.GetFloat(SavedFullnessKey), 0f, s.FullnessMax);
                tree.RemoveAttribute(SavedFullnessKey);
            }
            else
            {
                float max = hunger.MaxSaturation;
                if (Math.Abs(max - s.FullnessMax) > 0.01f && max > 0f)
                {
                    float cur = hunger.Saturation * s.FullnessMax / max;
                    hunger.MaxSaturation = s.FullnessMax;
                    hunger.Saturation = Math.Min(s.FullnessMax, cur);
                }
            }
            tree.SetBool(ScaledKey, true);
        }

        /// <summary>
        /// Tracking was switched off: puts a player back on vanilla's scale with vanilla values worked out from ours,
        /// so switching off cannot be used to dodge starvation. Satiety comes from the calorie reserve (at the
        /// digestion settings' kcal per satiety, so an empty reserve means an empty stomach), and the five food group bars
        /// from the reserves closest to them. Our own state is left as it is, and our fullness is kept, so
        /// switching tracking back on restores the player exactly as they were. Does nothing if we never changed the player.
        /// </summary>
        public static void ConvertToVanilla(Entity entity, NutrientRegistry nutrients, DigestionSettings s)
        {
            var hunger = entity?.GetBehavior<EntityBehaviorHunger>();
            var tree = entity?.WatchedAttributes.GetTreeAttribute(NutritionState.TreeKey);
            if (hunger == null || tree == null || s == null || !tree.GetBool(ScaledKey)) return;
            var state = NutritionState.Of(entity);
            float vmax = s.VanillaFullnessMax;

            tree.SetFloat(SavedFullnessKey, hunger.Saturation);
            hunger.MaxSaturation = vmax;
            var cal = nutrients.Get(NutrientCodes.Calories);
            float kcal = cal == null ? 0f : state.GetReserve(cal.Code) / 100f * cal.DailyTarget;
            hunger.Saturation = Math.Clamp(kcal / Math.Max(0.01f, s.KcalPerSatiety), 0f, vmax);

            float Bar(float percentOfDay) => Math.Clamp(percentOfDay / 100f, 0f, 1f) * vmax;
            float need = Math.Max(1f, s.ProteinNeedGrams);
            hunger.FruitLevel = Bar(state.GetReserve(NutrientCodes.VitaminC));
            hunger.VegetableLevel = Bar(state.GetReserve(NutrientCodes.VitaminK));
            hunger.GrainLevel = Bar(state.GetReserve(NutrientCodes.VitaminB));
            hunger.ProteinLevel = Bar(state.Protein24 / need * 100f);
            hunger.DairyLevel = Bar(state.GetReserve(NutrientCodes.Calcium));

            tree.SetBool(ScaledKey, false);
            state.TrackingOn = false;
            entity.WatchedAttributes.MarkPathDirty(NutritionState.TreeKey);
            hunger.UpdateNutrientHealthBoost();
        }
    }
}
