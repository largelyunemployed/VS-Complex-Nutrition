using System.Collections.Generic;
using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace ComplexNutrition.Core
{
    /// <summary>Multipliers for cooked dishes and pies, from config/cooking.json.</summary>
    public class CookingRules
    {
        public const string AssetPath = "config/cooking.json";

        /// <summary>Nutrient code to multiplier, applied to the summed ingredients of a cooked dish.</summary>
        [JsonProperty("cookedMultipliers")] public Dictionary<string, float> CookedMultipliers = new();

        /// <summary>Pie bake state (the last part of its code) to a multiplier on every nutrient.</summary>
        [JsonProperty("pieStateFactors")] public Dictionary<string, float> PieStateFactors = new();

        public float Cooked(string nutrient) => CookedMultipliers != null && CookedMultipliers.TryGetValue(nutrient, out var m) ? m : 1f;

        public static CookingRules Load(ICoreAPI api)
        {
            var asset = api.Assets.TryGet(new AssetLocation(ComplexNutritionModSystem.ModId, AssetPath));
            if (asset == null)
            {
                api.Logger.Warning("[complexnutrition] Missing {0}; cooked dishes get no cooking multipliers.", AssetPath);
                return new CookingRules();
            }
            return asset.ToObject<CookingRules>() ?? new CookingRules();
        }
    }
}
