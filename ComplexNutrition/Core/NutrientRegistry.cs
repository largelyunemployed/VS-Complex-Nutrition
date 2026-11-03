using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace ComplexNutrition.Core
{
    /// <summary>One tracked nutrient, as defined in config/nutrients.json.</summary>
    public class NutrientDefinition
    {
        /// <summary>Stable id used as the key in food profiles and (later) the player's reserves.</summary>
        [JsonProperty("code")] public string Code;

        /// <summary>Energy, Macro, FatSolubleVitamin, WaterSolubleVitamin or Mineral.</summary>
        [JsonProperty("group")] public string Group;

        /// <summary>Unit the food profiles store this nutrient in: kcal, g or %DV.</summary>
        [JsonProperty("unit")] public string Unit;

        /// <summary>Daily target in <see cref="Unit"/>. For %DV nutrients this is 100.</summary>
        [JsonProperty("dailyTarget")] public float DailyTarget;

        /// <summary>Order in tooltips and (later) UI lists.</summary>
        [JsonProperty("sortOrder")] public int SortOrder;

        /// <summary>Where the daily target comes from.</summary>
        [JsonProperty("source")] public string Source;

        /// <summary>Tracking: the most the body stores, in % of the daily target (100 = one day).</summary>
        [JsonProperty("cap")] public float Cap = 150;

        /// <summary>Tracking: reserve a new character starts with, in % of the daily target.</summary>
        [JsonProperty("startReserve")] public float StartReserve = 100;

        /// <summary>Tracking: how much is used per in-game day, in % of the daily target.
        /// Calories ignore this and follow the game's own hunger rate instead.</summary>
        [JsonProperty("drainPerDay")] public float DrainPerDay = 100;

        /// <summary>Tracking: intake in one sitting, in % of the daily target, above which
        /// the extra is absorbed at a falling rate.</summary>
        [JsonProperty("sittingLimit")] public float SittingLimit = 150;

        public string LangKey => "complexnutrition:nutrient-" + Code;

        public bool IsMicronutrient => Unit == "%DV";

        public bool IsCalories => Code == NutrientCodes.Calories;

        /// <summary>Converts an amount in <see cref="Unit"/> to % of the daily target.</summary>
        public float ToPercent(float amount) => DailyTarget > 0 ? amount / DailyTarget * 100f : 0f;
    }

    public class NutrientRegistryFile
    {
        [JsonProperty("nutrients")] public List<NutrientDefinition> Nutrients = new();
    }

    /// <summary>
    /// The list of nutrients the mod knows about. Loaded from JSON so the set can grow
    /// (or be patched by other mods) without code changes.
    /// </summary>
    public class NutrientRegistry
    {
        public const string AssetPath = "config/nutrients.json";

        private readonly Dictionary<string, NutrientDefinition> byCode = new();
        public IReadOnlyList<NutrientDefinition> All { get; private set; } = new List<NutrientDefinition>();

        public void Load(ICoreAPI api)
        {
            byCode.Clear();
            var asset = api.Assets.TryGet(new AssetLocation(ComplexNutritionModSystem.ModId, AssetPath));
            if (asset == null)
            {
                api.Logger.Error("[complexnutrition] Missing asset {0}; no nutrients registered.", AssetPath);
                All = new List<NutrientDefinition>();
                return;
            }

            var file = asset.ToObject<NutrientRegistryFile>();
            foreach (var def in file.Nutrients)
            {
                if (string.IsNullOrWhiteSpace(def.Code)) continue;
                if (byCode.ContainsKey(def.Code))
                {
                    api.Logger.Warning("[complexnutrition] Nutrient '{0}' is defined twice; keeping the first.", def.Code);
                    continue;
                }
                byCode[def.Code] = def;
            }
            All = byCode.Values.OrderBy(n => n.SortOrder).ToList();
        }

        public NutrientDefinition Get(string code) => code != null && byCode.TryGetValue(code, out var d) ? d : null;

        public bool Contains(string code) => code != null && byCode.ContainsKey(code);
    }
}
