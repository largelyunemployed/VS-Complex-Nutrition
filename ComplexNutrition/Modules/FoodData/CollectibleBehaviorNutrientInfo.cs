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
            : Lang.Get(Math.Abs(meal.Servings - 1f) < 0.001f ? "complexnutrition:tooltip-header-serving" : "complexnutrition:tooltip-header-servings",
                Fmt.Number(meal.Servings <= 0f ? 1f : meal.Servings));

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
            // v0.3.6 layout (Miles's pick, "A"): calories and fullness, then every macro, vitamin and mineral on three short lines.
            // Amounts keep their unit colours (grams green, kcal orange, vitamins and minerals yellow), as on the Stats panel.
            string Short(NutrientDefinition n) => Lang.HasTranslation("complexnutrition:nutrient-" + n.Code + "-short")
                ? Lang.Get("complexnutrition:nutrient-" + n.Code + "-short")
                : Lang.Get(n.LangKey);
            string first = Macro(NutrientCodes.Calories);
            if (digest != null && fullness > 0f)
            {
                string fill = Math.Round(fullness) < 1 ? "<1" : Math.Round(fullness).ToString("0");
                first += " · " + (Digestion.NoFullnessLimit
                    ? Lang.Get("complexnutrition:tooltip-fills-nolimit", fill)
                    : Lang.Get("complexnutrition:tooltip-fills", fill, Math.Round(digest.FullnessMax)));
            }
            sb.AppendLine(UnitColors.Paint(first));

            var macros = new[] { NutrientCodes.Carbohydrate, NutrientCodes.Fat, NutrientCodes.Protein, NutrientCodes.Fiber }
                .Select(c => nutrients.Get(c)).Where(n => n != null)
                .Select(n => Short(n) + " " + Macro(n.Code));
            sb.AppendLine(UnitColors.Paint(string.Join(" · ", macros)));

            // Vitamins and minerals always as % of a day's need, so each fits in a few characters.
            string Dv(NutrientDefinition n)
            {
                float dv = n.ToPercent(amount(n.Code));
                string text = dv > 0f && dv < 0.5f ? "<1%" : Math.Round(dv).ToString("#,##0") + "%";
                return Short(n) + " <font color=\"" + UnitColors.Micro + "\">" + text + "</font>";
            }
            var vitamins = nutrients.All.Where(n => n.IsMicronutrient && n.Group != "Mineral").OrderBy(Short, StringComparer.Ordinal).ToList();
            var minerals = nutrients.All.Where(n => n.Group == "Mineral").ToList();
            if (vitamins.Count > 0)
                sb.AppendLine(Lang.Get("complexnutrition:tooltip-vit") + " " + string.Join(" · ", vitamins.Select(Dv)));
            if (minerals.Count > 0)
                sb.AppendLine(string.Join(" · ", minerals.Select(Dv)));
            if (vitamins.Count + minerals.Count > 0) sb.AppendLine(Lang.Get("complexnutrition:tooltip-legend"));
            if (footer != null) sb.AppendLine(footer);
            return sb.ToString();
        }
    }
}
