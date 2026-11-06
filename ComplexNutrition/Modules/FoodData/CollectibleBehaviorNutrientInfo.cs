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
            if (!core.ShowTooltip) return;

            if (MealNutrition.IsMealContainer(collObj))
            {
                var meal = MealNutrition.Compute(core, world, inSlot?.Itemstack)?.AllServings();
                if (meal == null) return;
                dsc.Append(core.CanSeeNutrients(world, inSlot?.Itemstack)
                    ? BuildMealTooltip(core.Nutrients, meal, core.ShowPercent, withDebugInfo, core.ShortTooltipNow(withDebugInfo))
                    : BuildUndiscovered(MealHeader(meal), meal.Fullness, core.ShortTooltipNow(withDebugInfo)));
                return;
            }

            var resolved = core.Foods.Resolve(collObj);
            if (resolved == null) return;
            dsc.Append(core.CanSeeNutrients(world, inSlot?.Itemstack ?? new ItemStack(collObj))
                ? BuildTooltip(core.Nutrients, resolved, core.ShowPercent, withDebugInfo, collObj, core.ShortTooltipNow(withDebugInfo))
                : BuildUndiscovered(FoodHeader(resolved), Digestion.Settings == null ? 0f : Digestion.FoodFullness(Digestion.Settings, resolved, collObj), core.ShortTooltipNow(withDebugInfo)));
        }

        /// <summary>The tooltip block for a single food, amounts in % of a day when <paramref name="percent"/> is set. Also used by the /cnutrition info command.</summary>
        public static string BuildTooltip(NutrientRegistry nutrients, ResolvedFoodProfile food, bool percent, bool withDebugInfo, CollectibleObject coll = null, bool brief = false)
        {
            float fullness = Digestion.Settings == null ? 0f : Digestion.FoodFullness(Digestion.Settings, food, coll);
            string header = FoodHeader(food);

            string footer = null;
            if (food.IsFallback)
            {
                footer = Lang.Get("complexnutrition:tooltip-estimated");
            }
            else if (withDebugInfo && food.Profile.Source != null)
            {
                footer = Lang.Get("complexnutrition:tooltip-source", food.Profile.Source.Description, food.Profile.Source.FdcId);
            }
            return BuildBlock(nutrients, header, food.PerPortion, percent, withDebugInfo, footer, fullness, brief);
        }

        /// <summary>The tooltip block for a cooked dish or pie.</summary>
        public static string BuildMealTooltip(NutrientRegistry nutrients, MealNutritionResult meal, bool percent, bool withDebugInfo, bool brief = false)
        {
            string header = MealHeader(meal);

            string footer = Lang.Get(meal.Cooked ? "complexnutrition:tooltip-meal-cooked" : "complexnutrition:tooltip-meal-uncooked");
            if (meal.UnknownIngredients > 0)
            {
                footer += "\n" + Lang.Get("complexnutrition:tooltip-meal-unknown", meal.UnknownIngredients);
            }
            return BuildBlock(nutrients, header, code => meal.Totals.TryGetValue(code, out var v) ? v : 0f, percent, withDebugInfo, footer, meal.Fullness, brief);
        }

        private static string FoodHeader(ResolvedFoodProfile food) => food.IsLiquid
            ? Lang.Get("complexnutrition:tooltip-header-litre")
            : Lang.Get("complexnutrition:tooltip-header-portion", Math.Round(food.PortionGrams));

        private static string MealHeader(MealNutritionResult meal) => meal.IsPie
            ? Lang.Get("complexnutrition:tooltip-header-pie", meal.PieSlices)
            : meal.Scaled
                ? Lang.Get(meal.Servings < 1f ? "complexnutrition:tooltip-header-left" : "complexnutrition:tooltip-header-all", Fmt.Number(meal.Servings))
                : Lang.Get("complexnutrition:tooltip-header-serving");

        /// <summary>
        /// Discover Nutrition (v0.3.4): the block for a food the player hasn't eaten yet. Its nutrients are "???", but how filling
        /// it is still shows, since you can see how big it is.
        /// </summary>
        public static string BuildUndiscovered(string header, float fullness, bool brief)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            var digest = Digestion.Settings;
            string fill = digest != null && fullness > 0f ? (Math.Round(fullness) < 1 ? "<1" : Math.Round(fullness).ToString("0")) : null;
            if (brief)
            {
                sb.AppendLine(header + " " + Lang.Get("complexnutrition:tooltip-short", "???", fill == null ? "" : Lang.Get("complexnutrition:tooltip-short-fills", fill)));
                sb.AppendLine(Lang.Get("complexnutrition:tooltip-undiscovered-hint"));
                return sb.ToString();
            }
            sb.AppendLine(header);
            sb.AppendLine(Lang.Get("complexnutrition:tooltip-undiscovered"));
            if (fill != null) sb.AppendLine(Lang.Get("complexnutrition:tooltip-fullness", fill, Math.Round(digest.FullnessMax)));
            sb.AppendLine(Lang.Get("complexnutrition:tooltip-undiscovered-hint"));
            return sb.ToString();
        }

        /// <summary>
        /// The block: a header, the macros, the richest vitamins and minerals, fullness and a footer. With <paramref name="brief"/>
        /// (short tooltips, Ctrl not held) only the header with calories and fullness, and a hint to hold Ctrl.
        /// </summary>
        private static string BuildBlock(NutrientRegistry nutrients, string header, System.Func<string, float> rawAmount, bool percent, bool withDebugInfo, string footer, float fullness, bool brief = false)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine(header);
            // With digestion on, the body gets its calories from the macros (4 kcal per g of net carbs and protein,
            // 9 per g of fats), so that is the figure shown.
            var digest = Digestion.Settings;
            System.Func<string, float> amount = digest == null
                ? rawAmount
                : code => code == NutrientCodes.Calories ? digest.KcalFromMacros(rawAmount) : rawAmount(code);
            string Macro(string code) => nutrients.Get(code)?.FormatAmount(amount(code), percent) ?? "0";
            if (brief)
            {
                string fills = digest != null && fullness > 0f
                    ? Lang.Get("complexnutrition:tooltip-short-fills", Math.Round(fullness) < 1 ? "<1" : Math.Round(fullness).ToString("0"))
                    : "";
                sb.Clear();
                sb.AppendLine();
                sb.AppendLine(UnitColors.Paint(header + " " + Lang.Get("complexnutrition:tooltip-short", Macro(NutrientCodes.Calories), fills)));
                sb.AppendLine(Lang.Get("complexnutrition:tooltip-short-hint"));
                return sb.ToString();
            }
            // Amounts in their unit colours (grams green, kcal orange, mg and mcg yellow), as on the Stats panel.
            sb.AppendLine(UnitColors.Paint(Lang.Get(percent ? "complexnutrition:tooltip-macros-percent" : "complexnutrition:tooltip-macros",
                Macro(NutrientCodes.Calories),
                Macro(NutrientCodes.Protein),
                Macro(NutrientCodes.Carbohydrate),
                Macro(NutrientCodes.Fat),
                Macro(NutrientCodes.Fiber))));

            // The micronutrients that cover the most of a day's need, shown in the same units as the reserves.
            var top = nutrients.All
                .Where(n => n.IsMicronutrient)
                .Select(n => (def: n, amount: amount(n.Code), dv: n.ToPercent(amount(n.Code))))
                .Where(t => t.dv >= 1f)
                .OrderByDescending(t => t.dv)
                .Take(withDebugInfo ? int.MaxValue : TopMicronutrients)
                .Select(t => Lang.Get("complexnutrition:tooltip-dv", Lang.Get(t.def.LangKey), t.def.FormatAmount(t.amount, percent)))
                .ToList();
            if (top.Count > 0)
            {
                sb.AppendLine(UnitColors.Paint(Lang.Get(percent ? "complexnutrition:tooltip-richin" : "complexnutrition:tooltip-richin-amount", string.Join(", ", top))));
            }
            if (digest != null && fullness > 0f)
            {
                sb.AppendLine(Lang.Get("complexnutrition:tooltip-fullness", Math.Round(fullness) < 1 ? "<1" : Math.Round(fullness).ToString("0"), Math.Round(digest.FullnessMax)));
            }
            if (footer != null) sb.AppendLine(footer);
            return sb.ToString();
        }
    }
}
