using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace ComplexNutrition.Core
{
    /// <summary>One diet tier (since v0.4.3), from config/diet.json: the scores it covers and what it does to the player.</summary>
    public class DietTierDefinition
    {
        /// <summary>The tier's name in code and lang keys: "gettingBy" is shown with the lang key "diettier-gettingBy".</summary>
        [JsonProperty("code")] public string Code;

        /// <summary>The lowest diet score on this tier. The tier runs up to the next tier's lowest score.</summary>
        [JsonProperty("minScore")] public float MinScore;

        /// <summary>The diet score a character starts with, or drops to on "Lose one diet tier on death", when put on this tier.</summary>
        [JsonProperty("startScore")] public float StartScore;

        /// <summary>
        /// What the tier does to vanilla stats. Each one works like a nutrient effect's target (config/effects.json): value is the
        /// effect's strength as a share (+0.05 = a 5% bonus, -0.1 = a 10% penalty), and invert and perBaseHealth work the same way.
        /// </summary>
        [JsonProperty("effects")] public List<DietTierEffect> Effects = new();

        public string LangKey => "complexnutrition:diettier-" + Code;
    }

    /// <summary>One stat change of a diet tier: a nutrient effect target with a fixed strength.</summary>
    public class DietTierEffect : EffectTarget
    {
        /// <summary>The effect's strength, as a signed share: +0.05 is a 5% bonus, -0.1 a 10% penalty.</summary>
        [JsonProperty("value")] public float Value;
    }

    /// <summary>The diet score's rules (since v0.4.3), from config/diet.json.</summary>
    public class DietSettings
    {
        public const string AssetPath = "config/diet.json";

        /// <summary>Each midnight the diet score closes one part in this many of the gap to the day's score (7 = a seventh).</summary>
        [JsonProperty("followDays")] public float FollowDays = 7f;

        /// <summary>Days in a row the diet score must stay above (or below) the current tier before the player moves to the new tier.</summary>
        [JsonProperty("daysToChangeTier")] public int DaysToChangeTier = 3;

        /// <summary>How much each nutrient counts towards a day's score. A nutrient left out doesn't count.</summary>
        [JsonProperty("weights")] public Dictionary<string, float> Weights = new()
        {
            [NutrientCodes.Carbohydrate] = 1f,
            [NutrientCodes.Fat] = 1f,
            [NutrientCodes.Protein] = 2f,
            [NutrientCodes.Fiber] = 0.8f,
            [NutrientCodes.VitaminC] = 0.6f,
            [NutrientCodes.VitaminB] = 0.6f,
            [NutrientCodes.VitaminA] = 0.5f,
            [NutrientCodes.VitaminD] = 0.5f,
            [NutrientCodes.VitaminE] = 0.5f,
            [NutrientCodes.VitaminK] = 0.5f,
            [NutrientCodes.Calcium] = 0.5f,
            [NutrientCodes.Iron] = 0.5f,
            [NutrientCodes.Potassium] = 0.5f,
        };

        /// <summary>The tier a new character starts on, by its code.</summary>
        [JsonProperty("startTier")] public string StartTier = "gettingBy";

        /// <summary>
        /// Starting calories choices (by name, as in <see cref="WorldSettings.StartOptions"/>) that start a new character on a
        /// different tier: Brutal on Malnourished, Rock Bottom on Starving.
        /// </summary>
        [JsonProperty("startTierByStartingCalories")] public Dictionary<string, string> StartTierByStartingCalories = new()
        {
            ["Brutal"] = "malnourished",
            ["RockBottom"] = "starving",
        };

        /// <summary>The tiers, lowest first.</summary>
        [JsonProperty("tiers")] public List<DietTierDefinition> Tiers = new();

        public static DietSettings Load(ICoreAPI api)
        {
            var settings = ConfigAsset.Load<DietSettings>(api, AssetPath);
            settings.Tiers = (settings.Tiers ?? new()).Where(t => t != null && !string.IsNullOrEmpty(t.Code)).OrderBy(t => t.MinScore).ToList();
            return settings;
        }

        /// <summary>The number of a tier code (0 = the lowest), or -1 when there is no such tier.</summary>
        public int TierNumber(string code) => Tiers.FindIndex(t => t.Code == code);

        /// <summary>A tier by number, kept in range, or null when there are no tiers.</summary>
        public DietTierDefinition Tier(int number) => Tiers.Count == 0 ? null : Tiers[Math.Clamp(number, 0, Tiers.Count - 1)];

        /// <summary>The highest tier number whose lowest score is at or below <paramref name="score"/>.</summary>
        public int TierFor(float score)
        {
            int found = 0;
            for (int i = 0; i < Tiers.Count; i++)
            {
                if (score >= Tiers[i].MinScore) found = i;
            }
            return found;
        }

        /// <summary>The tier a new character starts on, for the world's Starting calories choice (its name, such as "Brutal").</summary>
        public int StartTierFor(string startingCaloriesName)
        {
            if (startingCaloriesName != null && StartTierByStartingCalories != null
                && StartTierByStartingCalories.TryGetValue(startingCaloriesName, out var code))
            {
                int special = TierNumber(code);
                if (special >= 0) return special;
            }
            return Math.Max(0, TierNumber(StartTier));
        }

        /// <summary>The highest score on a tier: one below the next tier's lowest, or 100 for the top tier.</summary>
        public float TopScore(int number) => number + 1 < Tiers.Count ? Tiers[number + 1].MinScore - 1f : 100f;
    }

    /// <summary>The tier a player is on and where it is heading, as kept in their state.</summary>
    public readonly struct DietTierProgress
    {
        /// <summary>The tier the player is on now, whose effects apply.</summary>
        public readonly int Tier;

        /// <summary>The tier the diet score points to, when it differs from <see cref="Tier"/>.</summary>
        public readonly int PendingTier;

        /// <summary>Days in a row the diet score has pointed above (or below) <see cref="Tier"/>.</summary>
        public readonly int PendingDays;

        public DietTierProgress(int tier, int pendingTier, int pendingDays)
        {
            Tier = tier;
            PendingTier = pendingTier;
            PendingDays = pendingDays;
        }
    }

    /// <summary>
    /// The diet score maths (since v0.4.3), on both sides. Each in-game day gets a score from 0 to 100: every nutrient digested
    /// (or, for vitamins and minerals, absorbed) earns its weight's share, up to a full day's need of it. Each midnight the diet
    /// score closes part of the gap to that day's score, and the tier follows the diet score once it has stayed past the tier's
    /// edge for a few days in a row.
    /// </summary>
    public static class DietScore
    {
        /// <summary>The loaded rules (set by the core on both sides).</summary>
        public static DietSettings Settings = new();

        /// <summary>The name our diet tier modifiers carry on a stat ("complexnutrition-diettier").</summary>
        public const string TierSource = "diettier";

        /// <summary>One effect for each stat any tier changes (the first tier's that names it), so a modifier can be taken off on a tier without it.</summary>
        public static List<DietTierEffect> TierStats(DietSettings settings)
        {
            var seen = new Dictionary<string, DietTierEffect>();
            foreach (var tier in settings.Tiers)
            {
                foreach (var e in tier.Effects)
                {
                    if (!string.IsNullOrEmpty(e.Stat) && !seen.ContainsKey(e.Stat)) seen[e.Stat] = e;
                }
            }
            return seen.Values.ToList();
        }

        /// <summary>The player's diet tier's change to a stat, as a share (0 when effects are off on this world or the tier leaves it alone).</summary>
        public static float TierEffect(NutritionState state, string stat)
        {
            if (state == null || !NutrientEffects.WorldOn) return 0f;
            var effect = Settings.Tier(state.DietTier)?.Effects.Find(e => e.Stat == stat);
            return effect == null ? 0f : NutrientEffects.StatDelta(effect, effect.Value, 0f);
        }

        /// <summary>The nutrients that count when they are digested (in the gut), not when they are eaten: carbs, fats, protein and fiber.</summary>
        public static readonly HashSet<string> CountWhenDigested = new()
        {
            NutrientCodes.Carbohydrate, NutrientCodes.Fat, NutrientCodes.Protein, NutrientCodes.Fiber,
        };

        /// <summary>
        /// What the in-game day <paramref name="day"/> has brought of a nutrient so far, in % of a day's need: digested for carbs,
        /// fats, protein and fiber, absorbed for vitamins and minerals, plus the vitamin D sunlight made that day.
        /// </summary>
        public static float Brought(NutritionState state, string code, int day)
        {
            if (state == null) return 0f;
            if (CountWhenDigested.Contains(code)) return state.GetDigestedToday(code);
            float absorbed = state.GetAbsorbedToday(code);
            if (code == NutrientEffects.Settings.SunNutrient && state.SunDay == day) absorbed += state.SunMadeToday;
            return absorbed;
        }

        /// <summary>A nutrient's share of a day's need, 0 to 1, from an amount in % of a day's need. More than a full day earns nothing extra.</summary>
        public static float Share(float percentOfDay) => Math.Clamp(percentOfDay / 100f, 0f, 1f);

        /// <summary>
        /// A day's score, 0 to 100: the weighted average of every weighted nutrient's <see cref="Share"/>.
        /// <paramref name="percentOfDay"/> gives what the day brought of a nutrient, in % of a day's need.
        /// </summary>
        public static float DayScore(DietSettings settings, System.Func<string, float> percentOfDay)
        {
            float weightSum = 0f;
            float earned = 0f;
            foreach (var kv in settings.Weights ?? new())
            {
                if (kv.Value <= 0f) continue;
                weightSum += kv.Value;
                earned += kv.Value * Share(percentOfDay(kv.Key));
            }
            return weightSum <= 0f ? 0f : 100f * earned / weightSum;
        }

        /// <summary>The diet score after a day: it closes one part in <see cref="DietSettings.FollowDays"/> of the gap to the day's score.</summary>
        public static float Follow(DietSettings settings, float score, float dayScore)
        {
            float parts = Math.Max(1f, settings.FollowDays);
            return Math.Clamp(score + (dayScore - score) / parts, 0f, 100f);
        }

        /// <summary>
        /// The tier after a day. When the diet score points to a different tier, the days are counted for as long as it keeps
        /// pointing the same way (above or below the current tier). Once the count reaches <see cref="DietSettings.DaysToChangeTier"/>
        /// the player moves to the tier the score points to that day. Pointing back at the current tier starts the count again.
        /// </summary>
        public static DietTierProgress NextTier(DietSettings settings, DietTierProgress before, float score)
        {
            int target = settings.TierFor(score);
            if (target == before.Tier) return new DietTierProgress(before.Tier, before.Tier, 0);
            bool sameWay = before.PendingDays > 0 && Math.Sign(before.PendingTier - before.Tier) == Math.Sign(target - before.Tier);
            int days = sameWay ? before.PendingDays + 1 : 1;
            if (days >= Math.Max(1, settings.DaysToChangeTier)) return new DietTierProgress(target, target, 0);
            return new DietTierProgress(before.Tier, target, days);
        }
    }
}
