using System.Collections.Generic;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

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

        /// <summary>
        /// Ingredient code pattern (wildcards allowed) to the gameplay factor its raw profile carries (since v0.3.6j: raw grain 0.6,
        /// dough 0.8). Cooking or baking undoes it, so grain and dough in a cooked dish give their full value.
        /// </summary>
        [JsonProperty("rawIngredientFactors")] public Dictionary<string, float> RawIngredientFactors = new();

        /// <summary>The multiplier that brings a raw ingredient back to its full value once cooked (1 for anything not listed).</summary>
        public float CookedRestore(AssetLocation code)
        {
            if (RawIngredientFactors == null || code == null) return 1f;
            foreach (var pair in RawIngredientFactors)
            {
                if (pair.Value > 0f && WildcardUtil.Match(new AssetLocation(pair.Key), code)) return 1f / pair.Value;
            }
            return 1f;
        }

        public float Cooked(string nutrient) => CookedMultipliers != null && CookedMultipliers.TryGetValue(nutrient, out var m) ? m : 1f;

        public static CookingRules Load(ICoreAPI api) => ConfigAsset.Load<CookingRules>(api, AssetPath);
    }
}
