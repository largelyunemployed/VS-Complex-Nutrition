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
