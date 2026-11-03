using ComplexNutrition.Core;
using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace ComplexNutrition.Modules.Tracking
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

        public static TrackingSettings Load(ICoreAPI api)
        {
            var asset = api.Assets.TryGet(new AssetLocation(ComplexNutritionModSystem.ModId, AssetPath));
            return asset?.ToObject<TrackingSettings>() ?? new TrackingSettings();
        }
    }
}
