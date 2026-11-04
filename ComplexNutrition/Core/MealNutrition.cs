using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ComplexNutrition.Core
{
    /// <summary>Nutrients of a meal container's contents, summed from its ingredients.</summary>
    public class MealNutritionResult
    {
        /// <summary>Nutrient code to amount, per serving (or for the whole pie stack, for pies).</summary>
        public Dictionary<string, float> Totals = new();

        /// <summary>Calories by the vanilla food category of each ingredient, on the same basis as <see cref="Totals"/>.</summary>
        public Dictionary<string, float> KcalByCategory = new();

        public bool IsPie;
        public int PieSlices;
        public float Servings;
        public bool Cooked;
        public int UnknownIngredients;

        /// <summary>Fullness of what <see cref="Totals"/> covers (a serving, or the pie), before spoilage; 0 while digestion is off.</summary>
        public float Fullness;
    }

    /// <summary>
    /// Cooked dishes are not profiled one by one: there are too many ingredient combinations.
    /// Instead a serving adds up its ingredients' profiles and then applies the cooking multipliers.
    /// </summary>
    public static class MealNutrition
    {
        public static bool IsMealContainer(CollectibleObject coll) => coll is IBlockMealContainer;

        public static MealNutritionResult Compute(ComplexNutritionModSystem core, IWorldAccessor world, ItemStack stack)
        {
            if (stack?.Collectible is not IBlockMealContainer container) return null;
            ItemStack[] contents = container.GetNonEmptyContents(world, stack);
            if (contents == null || contents.Length == 0) return null;

            var result = new MealNutritionResult();
            float stateFactor = 1f;

            if (stack.Collectible is BlockPie)
            {
                // A pie stores the whole pie's ingredients; pieSize is how many of its 4 slices are left.
                result.IsPie = true;
                result.PieSlices = Math.Max(1, stack.Attributes.GetAsInt("pieSize", 4));
                string state = stack.Collectible.Variant?["state"] ?? stack.Collectible.LastCodePart();
                result.Cooked = state != "raw";
                if (core.Cooking.PieStateFactors != null && core.Cooking.PieStateFactors.TryGetValue(state, out var f)) stateFactor = f;
                stateFactor *= result.PieSlices / 4f;
            }
            else
            {
                // Pots, bowls and crocks store one serving's ingredients plus a serving count.
                // Only a dish made from a cooking recipe counts as cooked (a crock of pickles does not).
                result.Servings = container.GetQuantityServings(world, stack);
                result.Cooked = container.GetRecipeCode(world, stack) != null;
            }

            foreach (var ingredient in contents)
            {
                var resolved = core.Foods.Resolve(ingredient.Collectible);
                if (resolved == null)
                {
                    result.UnknownIngredients++;
                    continue;
                }

                float portions;
                if (resolved.IsLiquid)
                {
                    var props = BlockLiquidContainerBase.GetContainableProps(ingredient);
                    float itemsPerLitre = props?.ItemsPerLitre ?? 100f;
                    portions = ingredient.StackSize / itemsPerLitre;
                }
                else
                {
                    portions = ingredient.StackSize;
                }

                foreach (var n in core.Nutrients.All)
                {
                    result.Totals.TryGetValue(n.Code, out var sum);
                    result.Totals[n.Code] = sum + resolved.PerPortion(n.Code) * portions;
                }

                string category = FoodDatabase.ReadVanilla(ingredient.Collectible).Category ?? "Unknown";
                result.KcalByCategory.TryGetValue(category, out var kcal);
                result.KcalByCategory[category] = kcal + resolved.PerPortion(NutrientCodes.Calories) * portions;
            }

            float kcalMul = stateFactor * (result.Cooked ? core.Cooking.Cooked(NutrientCodes.Calories) : 1f);
            foreach (var key in new List<string>(result.KcalByCategory.Keys)) result.KcalByCategory[key] *= kcalMul;

            foreach (var n in core.Nutrients.All)
            {
                float mul = stateFactor * (result.Cooked ? core.Cooking.Cooked(n.Code) : 1f);
                result.Totals[n.Code] = result.Totals.TryGetValue(n.Code, out var v) ? v * mul : 0f;
            }

            // Fullness follows weight, not the bake state; a pie's is for the slices left.
            float fullness = Digestion.MealFullness(core, world, null, stack, contents, null, false);
            result.Fullness = result.IsPie ? fullness * result.PieSlices / 4f : fullness;
            return result;
        }
    }
}
