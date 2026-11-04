using System;
using System.Collections.Generic;
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
        public static double[] ColorOf(NutrientDefinition n) =>
            GroupColors.TryGetValue(n?.Group ?? "", out var c) ? c : GuiStyle.FoodBarColor;

        /// <summary>"empty", or "absorbed in about 2.5 hours" for a gut pool.</summary>
        public static string GutHoursText(float hours, float reserve)
        {
            if (reserve <= 0.01f || hours <= 0f) return Lang.Get("complexnutrition:gut-empty");
            return Lang.Get("complexnutrition:gut-hours", hours < 10f ? hours.ToString("0.0") : Math.Round(hours).ToString("0"));
        }

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
