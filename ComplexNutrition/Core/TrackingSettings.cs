using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace ComplexNutrition.Core
{
    /// <summary>Tracking rules from config/tracking.json. Per-nutrient numbers (cap, start, drain, sitting limit) live in config/nutrients.json.</summary>
    public class TrackingSettings
    {
        public const string AssetPath = "config/tracking.json";

        /// <summary>Food eaten within this many in-game hours of the previous food counts as the same sitting.</summary>
        [JsonProperty("sittingHours")] public float SittingHours = 1f;

        /// <summary>
        /// How fast absorption falls above a nutrient's sitting limit. The extra x (in % of a day) adds
        /// scale x ln(1 + x / scale), so with 100 the first extra day's worth gives about 69%.
        /// </summary>
        [JsonProperty("diminishingScale")] public float DiminishingScale = 100f;

        /// <summary>In-game days the diet ledger keeps.</summary>
        [JsonProperty("ledgerDays")] public int LedgerDays = 28;

        /// <summary>Real seconds between reserve updates.</summary>
        [JsonProperty("updateSeconds")] public float UpdateSeconds = 3f;

        /// <summary>Reserves drain once at least this many in-game hours have passed since the last drain.</summary>
        [JsonProperty("minDrainHours")] public float MinDrainHours = 0.1f;

        /// <summary>The nutrition window lists the calorie tank as running low below this many days.</summary>
        [JsonProperty("lowCaloriesDays")] public float LowCaloriesDays = 1f;

        /// <summary>The nutrition window lists a nutrient of a <see cref="LowGroups"/> group as running low below this many days.</summary>
        [JsonProperty("lowDays")] public float LowDays = 2f;

        /// <summary>
        /// Nutrient groups that can run low. Carbs, fats, protein, fiber, vitamin C and the B vitamins refresh within a day or two,
        /// so they get no warning.
        /// </summary>
        [JsonProperty("lowGroups")] public string[] LowGroups = { "FatSolubleVitamin", "Mineral" };

        /// <summary>Real seconds between checks of what each player is holding, for the foods they know.</summary>
        [JsonProperty("heldScanSeconds")] public float HeldScanSeconds = 5f;

        public static TrackingSettings Load(ICoreAPI api) => ConfigAsset.Load<TrackingSettings>(api, AssetPath);
    }
}
