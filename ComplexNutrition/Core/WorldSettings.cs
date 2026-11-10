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
        /// <summary>
        /// Starting calories: how full a new character's calorie reserve is, as one of <see cref="StartOptions"/>. Until v0.4.2 it
        /// also set the vitamin and mineral stores; worlds without <see cref="StartStoresKey"/> still work that way.
        /// </summary>
        public const string StartConditionKey = "complexnutritionStart";

        /// <summary>
        /// Starting vitamin and mineral reserves (since v0.4.2): how full a new character's vitamin, mineral and fiber stores
        /// are, from the same <see cref="StartOptions"/> as Starting calories.
        /// </summary>
        public const string StartStoresKey = "complexnutritionStartStores";

        /// <summary>
        /// Respawn effects: Calories/Diet Tier (since v0.4.2): what dying does to the calorie reserve, one of <see cref="RespawnCaloriesOptions"/>.
        /// It replaced the old "complexnutritionRespawn" setting, which is no longer read; a world that had it gets the default.
        /// </summary>
        public const string RespawnCaloriesKey = "complexnutritionRespawnCalories";

        /// <summary>Respawn effects: Calories/Diet Tier: the reserve becomes a share of what it held at death, with a floor (tracking.json).</summary>
        public const string RespawnLose = "lose";
        /// <summary>Respawn effects: Calories/Diet Tier: the reserve is left as it was.</summary>
        public const string RespawnKeep = "keep";
        /// <summary>Respawn effects: Calories/Diet Tier: the reserve goes back to a fixed small amount, and the diet back to the starting tier (never up).</summary>
        public const string RespawnReset = "reset";
        /// <summary>Respawn effects: Calories/Diet Tier (since v0.4.3): calories as <see cref="RespawnLose"/>, and the diet drops one tier.</summary>
        public const string RespawnLoseTier = "losetier";

        /// <summary>
        /// The Respawn effects: Calories/Diet Tier choices, as values and the names their lang keys use ("worldconfig-complexnutritionRespawnCalories-Lose").
        /// worldconfig.json lists the same values and names (a test checks).
        /// </summary>
        public static readonly StartOption[] RespawnCaloriesOptions =
        {
            new(RespawnLose, "Lose"),
            new(RespawnKeep, "Keep"),
            new(RespawnReset, "Reset"),
            new(RespawnLoseTier, "LoseTier"),
        };

        /// <summary>The Respawn effects: Calories/Diet Tier choice a world gets when it has none.</summary>
        public const string DefaultRespawnCalories = RespawnLose;

        /// <summary>
        /// Keep vitamin and mineral reserves on death (since v0.4.2). On unless the world switches it off. While on, dying leaves
        /// the vitamin, mineral and fiber stores and the vitamin C and B vitamin day counts as they were.
        /// </summary>
        public const string KeepStoresKey = "complexnutritionKeepStores";

        /// <summary>Show the advanced nutrition display by default.</summary>
        public const string AdvancedDisplayKey = "complexnutritionAdvancedDisplay";

        /// <summary>Poultry and bushmeat in pots. Read by the recipe patches in patches/meat-in-pots.json when the world loads.</summary>
        public const string MeatInPotsKey = "complexnutritionMeatInPots";

        /// <summary>Diminishing returns on calories and nutrients (since v0.3.4). On unless the world switches it off.</summary>
        public const string DiminishingReturnsKey = "complexnutritionDiminishingReturns";

        /// <summary>Discover Nutrition (since v0.3.4): food tooltips show "???" until the player has eaten that food. Off by default.</summary>
        public const string DiscoverKey = "complexnutritionDiscover";

        /// <summary>
        /// Hide Poison Effects (since v0.4.1), a part of Discover Nutrition: while both are on, a food's health loss (a poisonous
        /// mushroom's "-8 hp") stays hidden in its tooltip until the player has eaten it once. Off by default.
        /// </summary>
        public const string HidePoisonKey = "complexnutritionHidePoison";

        /// <summary>How much a wild crop gives when harvested, in % of a farmed one (since v0.3.4; vanilla gives 25%).</summary>
        public const string WildHarvestKey = "complexnutritionWildHarvest";

        /// <summary>The wild harvest options, in % of a farmed crop's yield. worldconfig.json lists the same values (a test checks).</summary>
        public static readonly string[] WildHarvestOptions = { "25", "50", "75", "100" };

        /// <summary>The wild harvest a world gets when it has none: half a farmed crop, twice vanilla's quarter.</summary>
        public const string DefaultWildHarvest = "50";

        /// <summary>Nutrient effects (since v0.4.0). On unless the world switches them off.</summary>
        public const string EffectsKey = "complexnutritionEffects";

        /// <summary>The nutrients whose effects the world has switched off one by one (since v0.4.0), as codes separated by commas.</summary>
        public const string EffectsOffKey = "complexnutritionEffectsOff";

        /// <summary>Whether nutrient effects apply on this world (on when the world has no such setting).</summary>
        public static bool Effects(IWorldAccessor world) =>
            !string.Equals(Text(world, EffectsKey), "false", StringComparison.OrdinalIgnoreCase);

        /// <summary>The nutrients whose effects this world has switched off.</summary>
        public static System.Collections.Generic.HashSet<string> EffectsOff(IWorldAccessor world) => NutrientEffects.ParseOff(Text(world, EffectsOffKey));

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
        /// The start conditions, best first, for Starting calories and Starting vitamin and mineral reserves. Each value divided
        /// by <see cref="FullValue"/> is the share of a store it fills (Full 100%, Easy 70%, ... Rock Bottom 0.1%). Until v0.4.1
        /// the value was the calorie reserve in % of a day, when the reserve always held 10 days, so saved worlds keep the same
        /// numbers. worldconfig.json lists the same values and names for both settings (a test checks).
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

        /// <summary>The Starting vitamin and mineral reserves choice a new world gets (Normal).</summary>
        public const string DefaultStartStores = "500";

        /// <summary><see cref="DefaultStartStores"/> as a number.</summary>
        public static float DefaultStartStoresValue => Number(DefaultStartStores).Value;

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

        /// <summary>The world's Starting calories choice (one of <see cref="StartOptions"/>), or null when the world has none (the nutrient's startReserve applies).</summary>
        public static float? StartCalories(IWorldAccessor world) => Number(Text(world, StartConditionKey));

        /// <summary>
        /// The world's Starting vitamin and mineral reserves choice. A world from before v0.4.2 has none, and its stores follow
        /// Starting calories as they used to; null when it has neither (the nutrients' startReserve applies).
        /// </summary>
        public static float? StartStores(IWorldAccessor world) => Number(Text(world, StartStoresKey)) ?? StartCalories(world);

        /// <summary>The name of the world's Starting calories choice ("Normal", "Brutal", "RockBottom"...), or null when it has none.</summary>
        public static string StartCaloriesName(IWorldAccessor world)
        {
            float? value = StartCalories(world);
            if (value == null) return null;
            foreach (var option in StartOptions)
            {
                if (Number(option.Value) == value) return option.Name;
            }
            return null;
        }

        /// <summary>The world's Respawn effects: Calories/Diet Tier choice: lose, keep, reset or losetier. Lose for a world without one, or with a value we don't know.</summary>
        public static string RespawnCalories(IWorldAccessor world)
        {
            string choice = Text(world, RespawnCaloriesKey);
            return IsRespawnCaloriesOption(choice) ? choice : DefaultRespawnCalories;
        }

        public static bool IsRespawnCaloriesOption(string value) => Array.Exists(RespawnCaloriesOptions, o => o.Value == value);

        /// <summary>Whether dying leaves the vitamin, mineral and fiber stores alone (on when the world has no such setting).</summary>
        public static bool KeepStores(IWorldAccessor world) =>
            !string.Equals(Text(world, KeepStoresKey), "false", StringComparison.OrdinalIgnoreCase);

        /// <summary>Whether the world's Starting calories is Rock Bottom, so a reset on death leaves almost nothing too.</summary>
        public static bool RockBottomStart(IWorldAccessor world) => Text(world, StartConditionKey) == StartOptions[^1].Value;

        /// <summary>The Rock Bottom option's value as a number (its share of a store is <see cref="StoreShare"/> of this).</summary>
        public static float RockBottomValue => Number(StartOptions[^1].Value).Value;

        /// <summary>The share of each vitamin and mineral store that goes with a start or respawn option (Full 1, Easy 0.7, ...).</summary>
        public static float StoreShare(float optionValue) => Math.Clamp(optionValue / FullValue, 0f, 1f);

        private static float? Number(string text) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : null;
    }
}
