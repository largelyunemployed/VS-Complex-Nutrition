using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace ComplexNutrition.Modules.Tracking
{
    /// <summary>
    /// Server-side patches on vanilla's hunger behaviour, applied only while tracking runs:
    /// calories burn as satiety burned, fullness no longer burns with activity, starvation moves to the
    /// calorie tank, and the five category bars give no extra HP.
    /// </summary>
    public static class HungerPatches
    {
        private const string HarmonyId = "complexnutrition.tracking.hunger";
        private static Harmony harmony;
        private static TrackingModule module;

        public static void Apply(TrackingModule tracking)
        {
            module = tracking;
            if (harmony != null) return;
            harmony = new Harmony(HarmonyId);
            harmony.Patch(AccessTools.Method(typeof(EntityBehaviorHunger), "ReduceSaturation"),
                prefix: new HarmonyMethod(typeof(HungerPatches), nameof(ReduceSaturationPrefix)));
            harmony.Patch(AccessTools.Method(typeof(EntityBehaviorHunger), nameof(EntityBehaviorHunger.UpdateNutrientHealthBoost)),
                prefix: new HarmonyMethod(typeof(HungerPatches), nameof(HealthBoostPrefix)));
            harmony.Patch(AccessTools.Method(typeof(EntityBehaviorHunger), "SlowTick"),
                prefix: new HarmonyMethod(typeof(HungerPatches), nameof(SlowTickPrefix)));
        }

        public static void Remove()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            module = null;
        }

        private static IServerPlayer PlayerOf(EntityBehaviorHunger hunger)
        {
            if (hunger.entity is not EntityPlayer ep || hunger.entity.World.Side != EnumAppSide.Server) return null;
            return hunger.entity.World.PlayerByUid(ep.PlayerUID) as IServerPlayer;
        }

        /// <summary>
        /// Vanilla calls ReduceSaturation every 10 seconds (and on attacks) with a multiplier that already
        /// includes movement, sprinting and game speed; it then applies the global hunger speed and the
        /// hungerrate stat (cold, classes, later our own effects) and burns 10 satiety per unit.
        /// We burn the same amount as calories instead. Fullness drains on a fixed clock in the tracking
        /// tick, and the five category bars are not used, so vanilla's own step is skipped.
        /// </summary>
        public static bool ReduceSaturationPrefix(EntityBehaviorHunger __instance, float satLossMultiplier, ref bool __result)
        {
            if (module == null) return true;
            var player = PlayerOf(__instance);
            if (player == null || module.StateOf(__instance.entity) == null) return true;
            float satiety = satLossMultiplier * GlobalConstants.HungerSpeedModifier * __instance.entity.Stats.GetBlended("hungerrate") * 10f;
            module.BurnSatiety(player, satiety);
            __result = false;
            return false;
        }

        /// <summary>
        /// Vanilla's SlowTick also deals starvation damage at zero satiety. While tracking runs, starvation comes
        /// from an empty calorie tank instead (see the tracking tick), so an empty stomach is kept just above zero.
        /// The rest of SlowTick (the cold raising the hunger rate) runs as usual.
        /// </summary>
        public static void SlowTickPrefix(EntityBehaviorHunger __instance)
        {
            if (module == null || PlayerOf(__instance) == null || module.StateOf(__instance.entity) == null) return;
            if (__instance.Saturation <= 0f) __instance.Saturation = TrackingModule.EmptyStomach;
        }

        /// <summary>While tracking runs for this player, vanilla's +2.5 HP per full category bar is off.</summary>
        public static bool HealthBoostPrefix(EntityBehaviorHunger __instance)
        {
            if (module == null || PlayerOf(__instance) == null) return true;
            if (module.StateOf(__instance.entity) == null) return true;
            __instance.entity.GetBehavior<EntityBehaviorHealth>()?.SetMaxHealthModifiers("nutrientHealthMod", 0f);
            return false;
        }
    }
}
