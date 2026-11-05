using System;
using ComplexNutrition.Core;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ComplexNutrition.Modules.FoodData
{
    /// <summary>
    /// v0.3.3: short food tooltips open up while Ctrl is held. The game only rebuilds an open tooltip when the hovered
    /// stack changes (or Shift, with extended debug info), so a postfix on the mouse tooltip HUD's render rebuilds it
    /// when Ctrl goes down or up, the same way the game does for Shift. Client only; removed with the module.
    /// </summary>
    public static class TooltipCtrlPatch
    {
        private const string HarmonyId = "complexnutrition.fooddata.ctrl";
        private static Harmony harmony;
        private static ComplexNutritionModSystem core;
        private static bool ctrlWasDown;

        private static AccessTools.FieldRef<object, GuiElementItemstackInfo[]> elementsRef;
        private static AccessTools.FieldRef<object, int> backIndexRef;
        private static AccessTools.FieldRef<object, ItemSlot> slotRef;
        private static AccessTools.FieldRef<object, bool> dirtyRef;

        public static void Apply(ICoreClientAPI capi, ComplexNutritionModSystem system)
        {
            core = system;
            if (harmony != null) return;
            var type = AccessTools.TypeByName("Vintagestory.Client.NoObf.HudMouseTools");
            var render = type == null ? null : AccessTools.Method(type, "OnRenderGUI");
            try
            {
                elementsRef = AccessTools.FieldRefAccess<GuiElementItemstackInfo[]>(type, "itemstackinfoElements");
                backIndexRef = AccessTools.FieldRefAccess<int>(type, "currentBackElemIndex");
                slotRef = AccessTools.FieldRefAccess<ItemSlot>(type, "currentSlot");
                dirtyRef = AccessTools.FieldRefAccess<bool>(type, "dirty");
            }
            catch (Exception)
            {
                render = null;
            }
            if (render == null)
            {
                capi.Logger.Warning("[complexnutrition] Could not find the tooltip HUD; holding Ctrl won't open short food tooltips until you hover the food again.");
                return;
            }
            harmony = new Harmony(HarmonyId);
            harmony.Patch(render, postfix: new HarmonyMethod(typeof(TooltipCtrlPatch), nameof(Postfix)));
        }

        public static void Remove()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            core = null;
        }

        public static void Postfix(object __instance)
        {
            if (core?.Client?.ShortTooltips != true) return;
            var capi = core.ClientApi;
            if (capi == null) return;
            var keys = capi.Input.KeyboardKeyStateRaw;
            bool down = keys[(int)GlKeys.ControlLeft] || keys[(int)GlKeys.ControlRight];
            if (down == ctrlWasDown) return;
            ctrlWasDown = down;
            var slot = slotRef(__instance);
            if (slot?.Itemstack == null) return;
            var element = elementsRef(__instance)?[backIndexRef(__instance)];
            if (element == null) return;
            element.SetSourceSlot(null);
            if (element.SetSourceSlot(slot)) dirtyRef(__instance) = true;
        }
    }
}
