using System;
using System.Linq;
using System.Text;
using ComplexNutrition.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ComplexNutrition.Modules.FoodData
{
    /// <summary>Client-side tooltip block showing a food's nutrients per portion.</summary>
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
            var resolved = core.Foods.Resolve(collObj);
            if (resolved == null) return;
            dsc.Append(BuildTooltip(core.Nutrients, resolved, withDebugInfo));
        }

        /// <summary>The tooltip block for a food. Also used by the /cnutrition info command.</summary>
        public static string BuildTooltip(NutrientRegistry nutrients, ResolvedFoodProfile food, bool withDebugInfo)
        {
            var sb = new StringBuilder();
            sb.AppendLine();

            string header = food.IsLiquid
                ? Lang.Get("complexnutrition:tooltip-header-litre")
                : Lang.Get("complexnutrition:tooltip-header-portion", Math.Round(food.PortionGrams));
            sb.AppendLine(header);

            float kcal = food.PerPortion(NutrientCodes.Calories);
            sb.AppendLine(Lang.Get("complexnutrition:tooltip-macros",
                Math.Round(kcal),
                Fmt(food.PerPortion(NutrientCodes.Protein)),
                Fmt(food.PerPortion(NutrientCodes.Carbohydrate)),
                Fmt(food.PerPortion(NutrientCodes.Fat)),
                Fmt(food.PerPortion(NutrientCodes.Fiber))));

            var top = nutrients.All
                .Where(n => n.IsMicronutrient)
                .Select(n => (def: n, dv: food.PerPortion(n.Code)))
                .Where(t => t.dv >= 1f)
                .OrderByDescending(t => t.dv)
                .Take(withDebugInfo ? int.MaxValue : TopMicronutrients)
                .Select(t => Lang.Get("complexnutrition:tooltip-dv", Lang.Get(t.def.LangKey), Math.Round(t.dv)))
                .ToList();
            if (top.Count > 0)
            {
                sb.AppendLine(Lang.Get("complexnutrition:tooltip-richin", string.Join(", ", top)));
            }

            if (food.IsFallback)
            {
                sb.AppendLine(Lang.Get("complexnutrition:tooltip-estimated"));
            }
            else if (withDebugInfo && food.Profile.Source != null)
            {
                sb.AppendLine(Lang.Get("complexnutrition:tooltip-source", food.Profile.Source.Description, food.Profile.Source.FdcId));
            }

            return sb.ToString();
        }

        private static string Fmt(float grams) => grams >= 10 ? Math.Round(grams).ToString("0") : grams.ToString("0.#");
    }
}
