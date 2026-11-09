using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace ComplexNutrition.Core
{
    /// <summary>How a nutrient's store turns into an effect (v0.4.0).</summary>
    public static class EffectRules
    {
        /// <summary>No effect yet (carbs, fats, protein and fiber).</summary>
        public const string None = "none";

        /// <summary>A slow store: a bonus for every day stored, a penalty when it runs empty (vitamins A, D, E, K and the minerals).</summary>
        public const string LongTerm = "longTerm";

        /// <summary>A fast store: a penalty only after several days without a full day stored (vitamin C and the B vitamins).</summary>
        public const string Timed = "timed";
    }

    /// <summary>What a nutrient's effect does to one stat.</summary>
    public class EffectTarget
    {
        /// <summary>A vanilla entity stat (walkspeed, healingeffectivness, ...), or <see cref="NutrientEffects.PhysicalResistance"/>.</summary>
        [JsonProperty("stat")] public string Stat;

        /// <summary>The stat moves against the effect: a bonus lowers it (fall damage, hunger rate, butchering time).</summary>
        [JsonProperty("invert")] public bool Invert;

        /// <summary>
        /// The stat counts health points, not a share (maxhealthExtraPoints): the effect is that share of the player's base max health.
        /// </summary>
        [JsonProperty("perBaseHealth")] public bool PerBaseHealth;

        /// <summary>Lang key (complexnutrition domain) naming what changes, as the player sees it: "effect-walkspeed" = "walk speed".</summary>
        [JsonProperty("label")] public string Label;

        /// <summary>Show the stat's own change (fall damage -12%, hunger rate +10%) rather than the effect (butchering speed +12%).</summary>
        [JsonProperty("showStat")] public bool ShowStat;
    }

    public class NutrientEffectDefinition
    {
        [JsonProperty("rule")] public string Rule = EffectRules.None;
        [JsonProperty("targets")] public List<EffectTarget> Targets = new();
    }

    /// <summary>Effect rules from config/effects.json (v0.4.0).</summary>
    public class EffectSettings
    {
        public const string AssetPath = "config/effects.json";

        /// <summary>Long-term stores: % bonus for each day stored.</summary>
        [JsonProperty("bonusPerDay")] public float BonusPerDay = 1f;

        /// <summary>Long-term stores: the largest bonus, in %.</summary>
        [JsonProperty("maxBonus")] public float MaxBonus = 20f;

        /// <summary>The penalty, in %, of an empty long-term store or a timed nutrient at full strength.</summary>
        [JsonProperty("emptyPenalty")] public float EmptyPenalty = 10f;

        /// <summary>Timed nutrients: days without a full day stored before the penalty starts.</summary>
        [JsonProperty("timedStartDays")] public float TimedStartDays = 3f;

        /// <summary>Timed nutrients: days without a full day stored when the penalty reaches full strength.</summary>
        [JsonProperty("timedFullDays")] public float TimedFullDays = 5f;

        /// <summary>Timed nutrients: the share of the penalty when it starts.</summary>
        [JsonProperty("timedStartShare")] public float TimedStartShare = 0.5f;

        /// <summary>Timed nutrients show as running low once this many days have gone by without a full day stored.</summary>
        [JsonProperty("timedWarnDays")] public float TimedWarnDays = 2f;

        /// <summary>The nutrient sunlight makes (vitamin D).</summary>
        [JsonProperty("sunNutrient")] public string SunNutrient = NutrientCodes.VitaminD;

        /// <summary>In-game hours in full midday sun that make one day's worth.</summary>
        [JsonProperty("sunHoursPerDay")] public float SunHoursPerDay = 2f;

        /// <summary>The most sunlight makes in one in-game day, in days' worth.</summary>
        [JsonProperty("sunMaxDaysPerDay")] public float SunMaxDaysPerDay = 1f;

        [JsonProperty("nutrients")] public Dictionary<string, NutrientEffectDefinition> Nutrients = new();

        public NutrientEffectDefinition Of(string code) =>
            code != null && Nutrients != null && Nutrients.TryGetValue(code, out var def) && def != null ? def : null;

        /// <summary>The nutrients with an effect, in config order.</summary>
        public IEnumerable<string> WithEffects => (Nutrients ?? new()).Where(kv => kv.Value != null && kv.Value.Rule != EffectRules.None && kv.Value.Targets.Count > 0).Select(kv => kv.Key);

        public static EffectSettings Load(ICoreAPI api) => ConfigAsset.Load<EffectSettings>(api, AssetPath);
    }

    /// <summary>
    /// The effect maths (v0.4.0), on both sides: how strong each nutrient's effect is from its store, as a signed share
    /// (+0.12 = a 12% bonus, -0.1 = a 10% penalty). Applying it to the player is the tracking module's job.
    /// </summary>
    public static class NutrientEffects
    {
        /// <summary>The "stat" of vitamin D's effect: blunt, slashing and piercing damage taken, cut through the health behaviour's damage hook.</summary>
        public const string PhysicalResistance = "physicalResistance";

        /// <summary>The prefix of our stat modifiers' names, so they never clash with vanilla's or another mod's.</summary>
        public const string StatPrefix = "complexnutrition-";

        /// <summary>The loaded rules (set by the core on both sides).</summary>
        public static EffectSettings Settings = new();

        /// <summary>The world's Nutrient effects setting: on the server from the world, on a client from the server's settings.</summary>
        public static bool WorldOn = true;

        /// <summary>The nutrients whose effects the world has switched off.</summary>
        public static ISet<string> WorldOff = new HashSet<string>();

        /// <summary>A nutrient's effect on this world for this state: 0 when effects, or this nutrient's, are switched off.</summary>
        public static float Current(NutritionState state, string code) => WorldOn ? Strength(Settings, state, code, WorldOff) : 0f;

        /// <summary>
        /// A long-term store's effect from the days it holds: +<see cref="EffectSettings.BonusPerDay"/>% a day up to
        /// <see cref="EffectSettings.MaxBonus"/>%, and below one day a straight line down to -<see cref="EffectSettings.EmptyPenalty"/>% at empty.
        /// </summary>
        public static float LongTerm(EffectSettings s, float days)
        {
            days = Math.Max(0f, days);
            float atOne = s.BonusPerDay;
            if (days < 1f) return (-s.EmptyPenalty + (atOne + s.EmptyPenalty) * days) / 100f;
            return Math.Min(s.MaxBonus, s.BonusPerDay * days) / 100f;
        }

        /// <summary>
        /// A timed nutrient's effect from the days gone by without a full day stored: nothing before
        /// <see cref="EffectSettings.TimedStartDays"/>, then <see cref="EffectSettings.TimedStartShare"/> of the penalty, rising to all of it at
        /// <see cref="EffectSettings.TimedFullDays"/>.
        /// </summary>
        public static float Timed(EffectSettings s, float daysWithout)
        {
            if (daysWithout < s.TimedStartDays) return 0f;
            float span = Math.Max(0.001f, s.TimedFullDays - s.TimedStartDays);
            float t = Math.Clamp((daysWithout - s.TimedStartDays) / span, 0f, 1f);
            float share = s.TimedStartShare + (1f - s.TimedStartShare) * t;
            return -s.EmptyPenalty * share / 100f;
        }

        /// <summary>A nutrient's effect for this state, or 0 when it has none or effects are off for it.</summary>
        public static float Strength(EffectSettings s, NutritionState state, string code, ISet<string> off = null)
        {
            var def = s?.Of(code);
            if (def == null || state == null || (off != null && off.Contains(code))) return 0f;
            return def.Rule switch
            {
                EffectRules.LongTerm => LongTerm(s, state.GetReserve(code) / 100f),
                EffectRules.Timed => Timed(s, state.GetHoursWithoutFullDay(code) / 24f),
                _ => 0f,
            };
        }

        /// <summary>What one target adds to its stat for an effect of this strength (in health points for max health).</summary>
        public static float StatDelta(EffectTarget t, float strength, float baseMaxHealth)
        {
            float v = t.Invert ? -strength : strength;
            return t.PerBaseHealth ? v * baseMaxHealth : v;
        }

        /// <summary>The change the player sees for a target, in %: the stat's own change when <see cref="EffectTarget.ShowStat"/>, else the effect.</summary>
        public static float ShownPercent(EffectTarget t, float strength) => (t.ShowStat && t.Invert ? -strength : strength) * 100f;

        /// <summary>A timed nutrient's counter: hours of play without a full day stored, reset by reaching a full day.</summary>
        public static float NextHoursWithout(float hoursBefore, float reserve, float hours) =>
            reserve >= 100f ? 0f : Math.Max(0f, hoursBefore) + Math.Max(0f, hours);

        /// <summary>
        /// Vitamin D made by sunlight over <paramref name="hours"/>, in % of a day's need: <paramref name="exposure"/> (0 to 1, open sky
        /// times daylight) makes a day's worth in <see cref="EffectSettings.SunHoursPerDay"/> hours, up to what is left of today's limit.
        /// </summary>
        public static float SunMade(EffectSettings s, float exposure, float hours, float madeToday)
        {
            if (s.SunHoursPerDay <= 0f || exposure <= 0f || hours <= 0f) return 0f;
            float made = 100f * Math.Clamp(exposure, 0f, 1f) * hours / s.SunHoursPerDay;
            float left = Math.Max(0f, s.SunMaxDaysPerDay * 100f - madeToday);
            return Math.Min(made, left);
        }

        /// <summary>Physical damage after vitamin D's effect: blunt, slashing and piercing hits are cut by the bonus or raised by the penalty.</summary>
        public static float ResistDamage(float damage, EnumDamageType type, float strength)
        {
            if (type != EnumDamageType.BluntAttack && type != EnumDamageType.SlashingAttack && type != EnumDamageType.PiercingAttack) return damage;
            return Math.Max(0f, damage * (1f - strength));
        }

        /// <summary>The world's switched-off nutrients, from its setting (codes separated by commas).</summary>
        public static HashSet<string> ParseOff(string text) =>
            new((text ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries));

        public static string JoinOff(IEnumerable<string> codes) => string.Join(",", codes.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c));
    }
}
