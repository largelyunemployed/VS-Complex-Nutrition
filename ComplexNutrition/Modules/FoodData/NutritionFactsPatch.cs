using System;
using ComplexNutrition.Core;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ComplexNutrition.Modules.FoodData
{
    /// <summary>
    /// v0.3.6h, client only. Meal containers' held-item tooltips end with vanilla's "Nutrition Facts" block (fullness by food
    /// category), which only repeats the fullness our own block already gives, so it is left out while our tooltip shows it.
    /// The block is still built everywhere else (a placed pot's info, the handbook). Also widens item tooltips so the compact
    /// nutrient lines fit on one line each.
    /// </summary>
    public static class NutritionFactsPatch
    {
        private const string HarmonyId = "complexnutrition.fooddata.facts";
        /// <summary>The widest an item tooltip may grow (vanilla 415); it still shrinks to fit shorter text.</summary>
        public const int TooltipWidth = 600;
        private static Harmony harmony;
        private static ComplexNutritionModSystem core;
        private static int vanillaWidth = -1;
        [ThreadStatic] private static int inHeldInfo;

        public static void Apply(ComplexNutritionModSystem system)
        {
            core = system;
            if (vanillaWidth < 0) vanillaWidth = GuiElementItemstackInfo.BoxWidth;
            GuiElementItemstackInfo.BoxWidth = Math.Max(vanillaWidth, TooltipWidth);
            if (harmony != null) return;
            harmony = new Harmony(HarmonyId);
            var enter = new HarmonyMethod(typeof(NutritionFactsPatch), nameof(Enter));
            var leave = new HarmonyMethod(typeof(NutritionFactsPatch), nameof(Leave));
            foreach (var type in new[] { typeof(BlockMeal), typeof(BlockCookedContainer), typeof(BlockCrock), typeof(BlockPie) })
            {
                var m = AccessTools.DeclaredMethod(type, nameof(CollectibleObject.GetHeldItemInfo));
                if (m != null) harmony.Patch(m, prefix: enter, finalizer: leave);
            }
            var facts = AccessTools.Method(typeof(BlockMeal), nameof(BlockMeal.GetContentNutritionFacts),
                new[] { typeof(IWorldAccessor), typeof(ItemSlot), typeof(ItemStack[]), typeof(EntityAgent), typeof(bool), typeof(float), typeof(float) });
            harmony.Patch(facts, postfix: new HarmonyMethod(typeof(NutritionFactsPatch), nameof(Hide)));
        }

        public static void Remove()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            core = null;
            if (vanillaWidth >= 0) GuiElementItemstackInfo.BoxWidth = vanillaWidth;
        }

        public static void Enter() => inHeldInfo++;

        public static Exception Leave(Exception __exception)
        {
            inHeldInfo = Math.Max(0, inHeldInfo - 1);
            return __exception;
        }

        public static void Hide(ref string __result)
        {
            if (inHeldInfo > 0 && core != null && core.ShowTooltip && Digestion.Settings != null) __result = "";
        }
    }
}
