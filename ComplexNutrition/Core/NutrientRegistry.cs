using System;
using System.Collections.Generic;
using System.Globalization;
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

        /// <summary>Unit amounts are shown in: kcal, g, mg or mcg. Empty for nutrients only shown in %
        /// (the B vitamins, which are a mix of six).</summary>
        [JsonProperty("displayUnit")] public string DisplayUnit;

        /// <summary>For %DV nutrients: the amount in <see cref="DisplayUnit"/> that makes 100% of the daily value.</summary>
        [JsonProperty("dvAmount")] public float DvAmount;

        public string LangKey => "complexnutrition:nutrient-" + Code;

        public bool IsMicronutrient => Unit == "%DV";

        public bool IsCalories => Code == NutrientCodes.Calories;

        /// <summary>Converts an amount in <see cref="Unit"/> to % of the daily target.</summary>
        public float ToPercent(float amount) => DailyTarget > 0 ? amount / DailyTarget * 100f : 0f;

        /// <summary>A quantity given in % of the daily target, as text: "72 g", "1,920 kcal", "7.0 mg",
        /// or "37%" when <paramref name="percent"/> is set ("37% DV" for a nutrient with no unit to show).</summary>
        public string Format(float percentOfDaily, bool percent)
        {
            if (percent) return Number(percentOfDaily) + "%";
            if (string.IsNullOrEmpty(DisplayUnit)) return Number(percentOfDaily) + "% DV";
            float amount = IsMicronutrient ? percentOfDaily / 100f * DvAmount : percentOfDaily / 100f * DailyTarget;
            return Number(amount) + " " + DisplayUnit;
        }

        /// <summary>An amount in <see cref="Unit"/> (as food profiles store it), as text like <see cref="Format"/>.</summary>
        public string FormatAmount(float amountInUnit, bool percent) => Format(ToPercent(amountInUnit), percent);

        private static string Number(float v)
        {
            if (v >= 1000f) return Math.Round(v).ToString("#,0", CultureInfo.InvariantCulture);
            if (v >= 10f || v == 0f) return Math.Round(v).ToString("0", CultureInfo.InvariantCulture);
            return v.ToString("0.#", CultureInfo.InvariantCulture);
        }
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

        /// <summary>Show quantities in % of a day's need rather than grams and kcal (config "displayUnits").</summary>
        public bool ShowPercent { get; set; }

        public NutrientDefinition Get(string code) => code != null && byCode.TryGetValue(code, out var d) ? d : null;

        public bool Contains(string code) => code != null && byCode.ContainsKey(code);
    }
}
