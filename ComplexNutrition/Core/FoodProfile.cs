using System.Collections.Generic;
using Newtonsoft.Json;

namespace ComplexNutrition.Core
{
    /// <summary>Where a profile's numbers come from.</summary>
    public class FoodProfileSource
    {
        /// <summary>USDA FoodData Central id (https://fdc.nal.usda.gov/food-details/{fdcId}).</summary>
        [JsonProperty("fdcId")] public int FdcId;

        /// <summary>The FoodData Central description of the food used.</summary>
        [JsonProperty("description")] public string Description;

        /// <summary>Dataset, normally "SR Legacy".</summary>
        [JsonProperty("dataset")] public string Dataset;
    }

    /// <summary>
    /// A food's real-world nutrient profile. Values are per 100 g (per 100 ml for liquids);
    /// <see cref="PortionGrams"/> says how much one in-game portion weighs.
    /// </summary>
    public class FoodProfile
    {
        /// <summary>Item or block code this profile applies to. May contain wildcards (*).</summary>
        [JsonProperty("code")] public string Code;

        /// <summary>Weight of one in-game portion in grams. For liquids, grams per litre.</summary>
        [JsonProperty("portionGrams")] public float PortionGrams;

        /// <summary>"item" (one item or block) or "litre" (liquid portions).</summary>
        [JsonProperty("portionUnit")] public string PortionUnit = "item";

        /// <summary>Nutrient code to amount per 100 g, in the unit the registry gives for that nutrient.</summary>
        [JsonProperty("per100g")] public Dictionary<string, float> Per100g = new();

        [JsonProperty("source")] public FoodProfileSource Source;

        /// <summary>Free-text notes on how the match was chosen (substitutes, cooking state, and so on).</summary>
        [JsonProperty("notes")] public string Notes;

        public bool IsLiquid => PortionUnit == "litre";

        public float Per100(string nutrient) => Per100g != null && Per100g.TryGetValue(nutrient, out var v) ? v : 0f;

        public float PerPortion(string nutrient) => Per100(nutrient) * PortionGrams / 100f;
    }

    public class FoodProfileFile
    {
        [JsonProperty("profiles")] public List<FoodProfile> Profiles = new();
    }

    /// <summary>Category templates and calibration used for foods that have no profile of their own.</summary>
    public class FoodFallbackFile
    {
        /// <summary>
        /// Calories per point of vanilla satiety. Fallback portions are sized so that
        /// portion kcal = satiety x this ratio. Still to be calibrated against play.
        /// </summary>
        [JsonProperty("kcalPerSatiety")] public float KcalPerSatiety = 1f;

        /// <summary>Vanilla food category (Fruit, Vegetable, Protein, Grain, Dairy, Unknown) to template.</summary>
        [JsonProperty("byCategory")] public Dictionary<string, FoodProfile> ByCategory = new();
    }
}
