using System.Collections.Generic;

namespace ComplexNutrition.Core
{
    /// <summary>
    /// Settings stored in ModConfig/complexnutrition.json. Every module has its own switch;
    /// with every module off, the mod changes nothing in game.
    /// </summary>
    public class ComplexNutritionConfig
    {
        public const string FileName = "complexnutrition.json";

        /// <summary>Module id to enabled flag. Missing modules use their own default.</summary>
        public Dictionary<string, bool> Modules = new();

        /// <summary>Food data module: show the nutrient block in item tooltips.</summary>
        public bool ShowTooltip = true;

        /// <summary>
        /// How amounts are shown in tooltips, the Stats panel and /nutrition: "amount" (grams, kcal, mg or mcg)
        /// or "percent" (% of a day's need). Display only; each player's own file decides what they see.
        /// </summary>
        public string DisplayUnits = "amount";

        [Newtonsoft.Json.JsonIgnore]
        public bool ShowPercent => string.Equals(DisplayUnits, "percent", System.StringComparison.OrdinalIgnoreCase);

        /// <summary>Food data module: log every food that has no profile and uses a category template.</summary>
        public bool LogFallbacks = true;

        public bool IsModuleEnabled(string id, bool defaultValue)
        {
            return Modules != null && Modules.TryGetValue(id, out var on) ? on : defaultValue;
        }
    }
}
