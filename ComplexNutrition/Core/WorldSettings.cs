using System;
using System.Globalization;
using Vintagestory.API.Common;

namespace ComplexNutrition.Core
{
    /// <summary>One start condition: its value (the calorie tank in % of a day) and its name in the lang files.</summary>
    public readonly struct StartOption
    {
        public readonly string Value;
        public readonly string Name;

        public StartOption(string value, string name)
        {
            Value = value;
            Name = name;
        }

        /// <summary>Its name in the player's language.</summary>
        public string LangKey => "worldconfig-" + WorldSettings.StartConditionKey + "-" + Name;
    }

    /// <summary>
    /// Our world settings (see worldconfig.json) and the one way to read them. Set at world creation they are strings;
    /// set with /worldconfig or the settings menu they may be stored as numbers or booleans, so every read goes
    /// through text.
    /// </summary>
    public static class WorldSettings
    {
        /// <summary>The calorie tank of a new character, in % of a day.</summary>
        public const string StartConditionKey = "complexnutritionStart";

        /// <summary>Show the advanced nutrition display by default.</summary>
        public const string AdvancedDisplayKey = "complexnutritionAdvancedDisplay";

        /// <summary>Poultry and bushmeat in pots. Read by the recipe patches in patches/meat-in-pots.json when the world loads.</summary>
        public const string MeatInPotsKey = "complexnutritionMeatInPots";

        /// <summary>The start conditions, best first. worldconfig.json lists the same values and names (a test checks).</summary>
        public static readonly StartOption[] StartOptions =
        {
            new("1000", "Full"),
            new("700", "Easy"),
            new("500", "Normal"),
            new("300", "Hard"),
            new("100", "Brutal"),
            new("1", "RockBottom"),
        };

        /// <summary>The start condition a new world gets.</summary>
        public const string DefaultStart = "500";

        public static bool IsStartOption(string value) => Array.Exists(StartOptions, o => o.Value == value);

        /// <summary>A world setting as text (invariant culture), or null when the world does not have it.</summary>
        public static string Text(IWorldAccessor world, string key) =>
            Convert.ToString(world?.Config?[key]?.GetValue(), CultureInfo.InvariantCulture);

        /// <summary>A yes/no world setting: true only when it is set and reads "true".</summary>
        public static bool Bool(IWorldAccessor world, string key) =>
            string.Equals(Text(world, key), "true", StringComparison.OrdinalIgnoreCase);

        /// <summary>The world's start condition in % of a day, or null when the world has none (the nutrient's startReserve applies).</summary>
        public static float? StartCalories(IWorldAccessor world) =>
            float.TryParse(Text(world, StartConditionKey), NumberStyles.Float, CultureInfo.InvariantCulture, out float start) ? start : null;
    }
}
