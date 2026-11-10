using System;
using System.Collections.Generic;
using System.Linq;
using ComplexNutrition.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace ComplexNutrition.UI
{
    /// <summary>Display pieces shared by the Stats panel, the HUD, the nutrition window and the chat commands.</summary>
    public static class NutrientDisplay
    {
        /// <summary>Bar colours per nutrient group, in the game's muted palette.</summary>
        public static readonly Dictionary<string, double[]> GroupColors = new()
        {
            ["Energy"] = new[] { 0.80, 0.55, 0.22, 1.0 },
            ["Macro"] = GuiStyle.FoodBarColor,
            ["FatSolubleVitamin"] = new[] { 0.78, 0.68, 0.30, 1.0 },
            ["WaterSolubleVitamin"] = new[] { 0.86, 0.46, 0.24, 1.0 },
            ["Mineral"] = new[] { 0.45, 0.56, 0.66, 1.0 },
        };

        /// <summary>A nutrient's bar colour, from its group.</summary>
        /// <summary>Each macro's own colour (v0.3.6), used by its bars in the nutrition window and on the Stats panel alike.</summary>
        public static readonly Dictionary<string, double[]> MacroColors = new()
        {
            [NutrientCodes.Carbohydrate] = new[] { 0.224, 0.529, 0.898, 1.0 },   // #3987e5
            [NutrientCodes.Fat] = new[] { 0.851, 0.349, 0.149, 1.0 },            // #d95926
            [NutrientCodes.Protein] = new[] { 0.098, 0.620, 0.439, 1.0 },        // #199e70
            [NutrientCodes.Fiber] = new[] { 0.788, 0.522, 0.0, 1.0 },            // #c98500
        };

        public static double[] ColorOf(NutrientDefinition n) =>
            n != null && MacroColors.TryGetValue(n.Code, out var m) ? m
            : GroupColors.TryGetValue(n?.Group ?? "", out var c) ? c : GuiStyle.FoodBarColor;

        /// <summary>"empty", or "absorbed in about 2.5 hours" for a gut pool.</summary>
        public static string GutHoursText(float hours, float reserve)
        {
            if (reserve <= 0.01f || hours <= 0f) return Lang.Get("complexnutrition:gut-empty");
            return Lang.Get("complexnutrition:gut-hours", hours < 10f ? hours.ToString("0.0") : Math.Round(hours).ToString("0"));
        }

        // ---- Effects (v0.4.0) ----

        /// <summary>A signed percentage as "+12%" or "-5%" (one decimal under 10).</summary>
        public static string SignedPercent(float percent)
        {
            float a = Math.Abs(percent);
            string num = a >= 9.95f ? Math.Round(a).ToString("0") : a.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            if (num == "0") return "0%";
            return (percent < 0 ? "-" : "+") + num + "%";
        }

        /// <summary>
        /// What an effect of this strength does, as the player sees it: "+12% ranged accuracy", or for several stats
        /// "+5% walk speed, +5% melee damage, ...". Max health adds its points when <paramref name="baseHealth"/> is given.
        /// </summary>
        public static string EffectText(string code, float strength, float baseHealth = 0f)
        {
            var def = NutrientEffects.Settings.Of(code);
            if (def == null) return "";
            return string.Join(", ", def.Targets.Select(t => TargetText(t, strength, baseHealth)));
        }

        /// <summary>One stat change as the player sees it: "+5% walk speed", or "+10% max health (+1.5 HP)" with the base health known.</summary>
        public static string TargetText(EffectTarget t, float strength, float baseHealth = 0f)
        {
            string label = Lang.Get("complexnutrition:" + t.Label);
            string pct = SignedPercent(NutrientEffects.ShownPercent(t, strength));
            if (t.PerBaseHealth && baseHealth > 0f)
            {
                float hp = strength * baseHealth;
                string hpText = (hp < 0 ? "-" : "+") + Math.Abs(hp).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
                return Lang.Get("complexnutrition:effect-change-hp", pct, label, hpText);
            }
            return Lang.Get("complexnutrition:effect-change", pct, label);
        }

        /// <summary>What a diet tier does (since v0.4.3), as "+5% healing, +5% max health (+0.8 HP), ...", or "" for a tier that does nothing.</summary>
        public static string TierEffectText(DietTierDefinition tier, float baseHealth = 0f) =>
            tier == null ? "" : string.Join(", ", tier.Effects.Where(e => Math.Abs(e.Value) > 0.0005f).Select(e => TargetText(e, e.Value, baseHealth)));

        /// <summary>
        /// The rule behind a nutrient's effect, in one or two sentences: for a long-term store what a full and an empty store do,
        /// for a timed one when the penalty starts and how bad it gets. Empty for a nutrient without an effect.
        /// </summary>
        public static string EffectRule(string code, NutrientDefinition n)
        {
            var s = NutrientEffects.Settings;
            var def = s.Of(code);
            if (def == null || def.Rule == EffectRules.None || def.Targets.Count == 0) return "";
            if (def.Rule == EffectRules.Timed)
            {
                return Lang.Get("complexnutrition:effect-rule-timed",
                    Num(s.TimedStartDays), EffectText(code, NutrientEffects.Timed(s, s.TimedStartDays)),
                    EffectText(code, NutrientEffects.Timed(s, s.TimedFullDays)), Num(s.TimedFullDays));
            }
            float fullDays = n == null ? s.MaxBonus / Math.Max(0.01f, s.BonusPerDay) : n.Cap / 100f;
            return Lang.Get("complexnutrition:effect-rule-longterm",
                EffectText(code, NutrientEffects.LongTerm(s, fullDays)), Num(fullDays),
                Num(s.BonusPerDay) + "%", EffectText(code, NutrientEffects.LongTerm(s, 0f)));
        }

        /// <summary>
        /// Where a nutrient's effect stands now: "Effect now: +12% ranged accuracy", with a timed nutrient's count of days,
        /// or why there is none (switched off, a full day stored, not yet).
        /// </summary>
        public static string EffectNow(string code, NutritionState state, float baseHealth = 0f)
        {
            var s = NutrientEffects.Settings;
            var def = s.Of(code);
            if (def == null || def.Rule == EffectRules.None || def.Targets.Count == 0 || state == null) return "";
            if (!NutrientEffects.WorldOn) return Lang.Get("complexnutrition:effect-now-worldoff");
            if (NutrientEffects.WorldOff.Contains(code)) return Lang.Get("complexnutrition:effect-now-off");
            float strength = NutrientEffects.Current(state, code);
            if (def.Rule == EffectRules.Timed)
            {
                float days = state.GetHoursWithoutFullDay(code) / 24f;
                if (days <= 0f) return Lang.Get("complexnutrition:effect-now-timed-full");
                if (strength == 0f) return Lang.Get("complexnutrition:effect-now-timed-waiting", Num(days), Num(s.TimedStartDays));
                return Lang.Get("complexnutrition:effect-now-timed", EffectText(code, strength, baseHealth), Num(days));
            }
            return Lang.Get("complexnutrition:effect-now", EffectText(code, strength, baseHealth));
        }

        private static string Num(float v) => v >= 10f ? Math.Round(v).ToString("0") : v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// A nutrient's name for a bar label: minerals carry their symbol, as in "Calcium (Ca)" (0.4.2d), to match the
        /// Ca · Fe · K line in food tooltips; everything else is just its name.
        /// </summary>
        public static string BarName(NutrientDefinition nutrient) => nutrient.Group == "Mineral"
            ? Lang.Get("complexnutrition:stats-mineral-label", Lang.Get(nutrient.LangKey), Lang.Get("complexnutrition:nutrient-" + nutrient.Code + "-short"))
            : Lang.Get(nutrient.LangKey);

        /// <summary>A reserve in % of a day as "0.6 days" or "12 days".</summary>
        public static string DaysText(float percent)
        {
            float days = percent / 100f;
            return days < 10f
                ? Lang.Get("complexnutrition:reserve-days", days.ToString("0.0"))
                : Lang.Get("complexnutrition:reserve-days", Math.Round(days).ToString("0"));
        }
    }
}
