using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace ComplexNutrition.Modules.Tracking
{
    /// <summary>
    /// Server-side patches on vanilla's hunger behaviour, applied only while tracking runs:
    /// calories burn as satiety burns, and the five category bars give no extra HP.
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
        /// We burn the same amount as calories, even while a meal's satiety delay holds the satiety bar still.
        /// </summary>
        public static void ReduceSaturationPrefix(EntityBehaviorHunger __instance, float satLossMultiplier)
        {
            if (module == null) return;
            var player = PlayerOf(__instance);
            if (player == null) return;
            float satiety = satLossMultiplier * GlobalConstants.HungerSpeedModifier * __instance.entity.Stats.GetBlended("hungerrate") * 10f;
            module.BurnSatiety(player, satiety);
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
