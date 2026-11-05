using System;
using System.Globalization;
using Vintagestory.API.Common;

namespace ComplexNutrition.Core
{
    /// <summary>One start condition: its value (the calorie reserve in % of a day) and its name in the lang files.</summary>
    public readonly struct StartOption
    {
        public readonly string Value;
        public readonly string Name;

        public StartOption(string value, string name)
        {
            Value = value;
            Name = name;
        }

        /// <summary>Its name in the player's language, for the world setting with this key (start or respawn).</summary>
        public string LangKey(string settingKey) => "worldconfig-" + settingKey + "-" + Name;
    }

    /// <summary>
    /// Our world settings (see worldconfig.json) and the one way to read them. Set at world creation they are strings;
    /// set with /worldconfig or the settings menu they may be stored as numbers or booleans, so every read goes
    /// through text.
    /// </summary>
    public static class WorldSettings
    {
        /// <summary>The calorie reserve of a new character, in % of a day.</summary>
        public const string StartConditionKey = "complexnutritionStart";

        /// <summary>The calorie reserve a character respawns with, in % of a day, from the same options (since v0.3.2).</summary>
        public const string RespawnConditionKey = "complexnutritionRespawn";

        /// <summary>Show the advanced nutrition display by default.</summary>
        public const string AdvancedDisplayKey = "complexnutritionAdvancedDisplay";

        /// <summary>Poultry and bushmeat in pots. Read by the recipe patches in patches/meat-in-pots.json when the world loads.</summary>
        public const string MeatInPotsKey = "complexnutritionMeatInPots";

        /// <summary>Diminishing returns on calories and nutrients (since v0.3.4). On unless the world switches it off.</summary>
        public const string DiminishingReturnsKey = "complexnutritionDiminishingReturns";

        /// <summary>Discover Nutrition (since v0.3.4): food tooltips show "???" until the player has eaten that food. Off by default.</summary>
        public const string DiscoverKey = "complexnutritionDiscover";

        /// <summary>How much a wild crop gives when harvested, in % of a farmed one (since v0.3.4; vanilla gives 25%).</summary>
        public const string WildHarvestKey = "complexnutritionWildHarvest";

        /// <summary>The wild harvest options, in % of a farmed crop's yield. worldconfig.json lists the same values (a test checks).</summary>
        public static readonly string[] WildHarvestOptions = { "25", "50", "75", "100" };

        /// <summary>The wild harvest a world gets when it has none: half a farmed crop, twice vanilla's quarter.</summary>
        public const string DefaultWildHarvest = "50";

        /// <summary>The stomach's size in fullness (since v0.3.5): 0 to 1,500, where 0 means no limit.</summary>
        public const string MaxFullnessKey = "complexnutritionMaxFullness";
        public const int DefaultMaxFullness = 300;
        /// <summary>The slider's top, and the fullness bar's top when there is no limit.</summary>
        public const int MaxFullnessTop = 1500;
        public const int MaxFullnessStep = 50;

        /// <summary>The world's Max fullness, 0 (no limit) to 1,500; 300 for worlds without the setting.</summary>
        public static int MaxFullness(IWorldAccessor world)
        {
            float v = Number(Text(world, MaxFullnessKey)) ?? DefaultMaxFullness;
            return (int)Math.Clamp(Math.Round(v), 0, MaxFullnessTop);
        }

        /// <summary>
        /// The start conditions, best first, also used for respawning. Each is the calorie reserve in % of a day; vitamins and
        /// minerals start at the same share of their own stores (Full 100%, Easy 70%, ... Rock Bottom 0.1%).
        /// worldconfig.json lists the same values and names for both settings (a test checks).
        /// </summary>
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

        /// <summary>The respawn condition a world gets when it has none (Brutal).</summary>
        public const string DefaultRespawn = "100";

        /// <summary>The calorie value of the Full option: a full reserve. Other options are this share of every store.</summary>
        public const float FullValue = 1000f;

        public static bool IsStartOption(string value) => Array.Exists(StartOptions, o => o.Value == value);

        /// <summary>A world setting as text (invariant culture), or null when the world does not have it.</summary>
        public static string Text(IWorldAccessor world, string key) =>
            Convert.ToString(world?.Config?[key]?.GetValue(), CultureInfo.InvariantCulture);

        /// <summary>A yes/no world setting: true only when it is set and reads "true".</summary>
        public static bool Bool(IWorldAccessor world, string key) =>
            string.Equals(Text(world, key), "true", StringComparison.OrdinalIgnoreCase);

        /// <summary>Whether diminishing returns apply on this world (on when the world has no such setting).</summary>
        public static bool DiminishingReturns(IWorldAccessor world) =>
            !string.Equals(Text(world, DiminishingReturnsKey), "false", StringComparison.OrdinalIgnoreCase);

        /// <summary>A wild crop's harvest as a share of a farmed one's (0.25 is vanilla).</summary>
        public static float WildHarvestShare(IWorldAccessor world)
        {
            float pct = Number(Text(world, WildHarvestKey)) ?? Number(DefaultWildHarvest).Value;
            return Math.Clamp(pct, 0f, 100f) / 100f;
        }

        /// <summary>The world's start condition in % of a day, or null when the world has none (the nutrient's startReserve applies).</summary>
        public static float? StartCalories(IWorldAccessor world) => Number(Text(world, StartConditionKey));

        /// <summary>The world's respawn condition in % of a day; Brutal (1 day) for worlds without one.</summary>
        public static float RespawnCalories(IWorldAccessor world) => Number(Text(world, RespawnConditionKey)) ?? Number(DefaultRespawn).Value;

        /// <summary>The share of each vitamin and mineral store that goes with a start or respawn option (Full 1, Easy 0.7, ...).</summary>
        public static float StoreShare(float optionValue) => Math.Clamp(optionValue / FullValue, 0f, 1f);

        private static float? Number(string text) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : null;
    }
}
