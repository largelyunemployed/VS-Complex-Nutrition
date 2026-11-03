using System.Text;
using ComplexNutrition.Core;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ComplexNutrition.Modules.FoodData
{
    /// <summary>
    /// BlockPie writes its own tooltip without running collectible behaviours, so the nutrient
    /// block is added to it with a Harmony postfix instead. Removed again when the module is disabled.
    /// </summary>
    public static class PieTooltipPatch
    {
        private const string HarmonyId = "complexnutrition.fooddata.pie";
        private static Harmony harmony;
        private static ComplexNutritionModSystem core;

        public static void Apply(ComplexNutritionModSystem system)
        {
            core = system;
            if (harmony != null) return;
            harmony = new Harmony(HarmonyId);
            var target = AccessTools.Method(typeof(BlockPie), nameof(BlockPie.GetHeldItemInfo));
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(PieTooltipPatch), nameof(Postfix)));
        }

        public static void Remove()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            core = null;
        }

        public static void Postfix(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            if (core == null || !core.Config.ShowTooltip) return;
            var meal = MealNutrition.Compute(core, world, inSlot?.Itemstack);
            if (meal != null) dsc.Append(CollectibleBehaviorNutrientInfo.BuildMealTooltip(core.Nutrients, meal, withDebugInfo));
        }
    }
}
