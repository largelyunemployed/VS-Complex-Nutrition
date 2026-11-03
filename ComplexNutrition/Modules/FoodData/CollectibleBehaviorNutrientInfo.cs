using System;
using System.Linq;
using System.Text;
using ComplexNutrition.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ComplexNutrition.Modules.FoodData
{
    /// <summary>Tooltip block showing a food's nutrients per portion, or a dish's per serving.</summary>
    public class CollectibleBehaviorNutrientInfo : CollectibleBehavior
    {
        private const int TopMicronutrients = 3;
        private readonly ComplexNutritionModSystem core;

        public CollectibleBehaviorNutrientInfo(CollectibleObject collObj, ComplexNutritionModSystem core) : base(collObj)
        {
            this.core = core;
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            if (!core.Config.ShowTooltip) return;

            if (MealNutrition.IsMealContainer(collObj))
            {
                var meal = MealNutrition.Compute(core, world, inSlot?.Itemstack);
                if (meal != null) dsc.Append(BuildMealTooltip(core.Nutrients, meal, withDebugInfo));
                return;
            }

            var resolved = core.Foods.Resolve(collObj);
            if (resolved == null) return;
            dsc.Append(BuildTooltip(core.Nutrients, resolved, withDebugInfo));
        }

        /// <summary>The tooltip block for a single food. Also used by the /cnutrition info command.</summary>
        public static string BuildTooltip(NutrientRegistry nutrients, ResolvedFoodProfile food, bool withDebugInfo)
        {
            string header = food.IsLiquid
                ? Lang.Get("complexnutrition:tooltip-header-litre")
                : Lang.Get("complexnutrition:tooltip-header-portion", Math.Round(food.PortionGrams));

            string footer = null;
            if (food.IsFallback)
            {
                footer = Lang.Get("complexnutrition:tooltip-estimated");
            }
            else if (withDebugInfo && food.Profile.Source != null)
            {
                footer = Lang.Get("complexnutrition:tooltip-source", food.Profile.Source.Description, food.Profile.Source.FdcId);
            }
            return BuildBlock(nutrients, header, food.PerPortion, withDebugInfo, footer);
        }

        /// <summary>The tooltip block for a cooked dish or pie.</summary>
        public static string BuildMealTooltip(NutrientRegistry nutrients, MealNutritionResult meal, bool withDebugInfo)
        {
            string header = meal.IsPie
                ? Lang.Get("complexnutrition:tooltip-header-pie", meal.PieSlices)
                : Lang.Get("complexnutrition:tooltip-header-serving");

            string footer = Lang.Get(meal.Cooked ? "complexnutrition:tooltip-meal-cooked" : "complexnutrition:tooltip-meal-uncooked");
            if (meal.UnknownIngredients > 0)
            {
                footer += "\n" + Lang.Get("complexnutrition:tooltip-meal-unknown", meal.UnknownIngredients);
            }
            return BuildBlock(nutrients, header, code => meal.Totals.TryGetValue(code, out var v) ? v : 0f, withDebugInfo, footer);
        }

        private static string BuildBlock(NutrientRegistry nutrients, string header, System.Func<string, float> amount, bool withDebugInfo, string footer)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine(header);
            sb.AppendLine(Lang.Get("complexnutrition:tooltip-macros",
                Math.Round(amount(NutrientCodes.Calories)),
                Fmt(amount(NutrientCodes.Protein)),
                Fmt(amount(NutrientCodes.Carbohydrate)),
                Fmt(amount(NutrientCodes.Fat)),
                Fmt(amount(NutrientCodes.Fiber))));

            var top = nutrients.All
                .Where(n => n.IsMicronutrient)
                .Select(n => (def: n, dv: amount(n.Code)))
                .Where(t => t.dv >= 1f)
                .OrderByDescending(t => t.dv)
                .Take(withDebugInfo ? int.MaxValue : TopMicronutrients)
                .Select(t => Lang.Get("complexnutrition:tooltip-dv", Lang.Get(t.def.LangKey), Math.Round(t.dv)))
                .ToList();
            if (top.Count > 0)
            {
                sb.AppendLine(Lang.Get("complexnutrition:tooltip-richin", string.Join(", ", top)));
            }
            if (footer != null) sb.AppendLine(footer);
            return sb.ToString();
        }

        private static string Fmt(float grams) => grams >= 10 ? Math.Round(grams).ToString("0") : grams.ToString("0.#");
    }
}
