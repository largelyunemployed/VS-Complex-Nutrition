using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ComplexNutrition.Core
{
    /// <summary>What a player just ate, as the core works it out. Published on the server for every food, drink, meal and pie slice.</summary>
    public class FoodEatenInfo
    {
        public IServerPlayer Player;

        /// <summary>The item, drink or dish eaten, as it was before eating. Null when another mod handed out satiety directly.</summary>
        public ItemStack Source;

        /// <summary>Nutrient code to the amount eaten, in the registry's unit (kcal, g or %DV).</summary>
        public Dictionary<string, float> Nutrients = new();

        /// <summary>Calories eaten by vanilla food category (Fruit, Vegetable, Grain, Protein, Dairy, Unknown).</summary>
        public Dictionary<string, float> KcalByCategory = new();

        /// <summary>True when the amounts come from a category estimate rather than a food profile.</summary>
        public bool Estimated;

        public float Get(string nutrient) => Nutrients.TryGetValue(nutrient, out var v) ? v : 0f;
    }
}
