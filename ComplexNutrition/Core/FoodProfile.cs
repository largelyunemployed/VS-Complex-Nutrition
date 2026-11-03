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

        /// <summary>"item" (one item or block) or "litre" (liquid portions). Left out, it means "item",
        /// or the unit of the <see cref="Like"/> food.</summary>
        [JsonProperty("portionUnit")] public string PortionUnit;

        /// <summary>
        /// Optional item code of a food that already has a profile (for example "game:fruit-blueberry").
        /// Its per-100 g values are copied, then any <see cref="Per100g"/> entries here replace them,
        /// and <see cref="Scale"/> is applied. portionGrams and portionUnit are copied too when left out.
        /// Meant for other mods' foods that are close to a vanilla one.
        /// </summary>
        [JsonProperty("like")] public string Like;

        /// <summary>Optional per-nutrient multipliers applied after <see cref="Like"/> ("all" scales every nutrient).</summary>
        [JsonProperty("scale")] public Dictionary<string, float> Scale;

        /// <summary>
        /// When two files give a profile for the same code, the higher priority wins (default 0;
        /// this mod's own files use 0). Use a higher number to override a value this mod ships.
        /// </summary>
        [JsonProperty("priority")] public int Priority;

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
