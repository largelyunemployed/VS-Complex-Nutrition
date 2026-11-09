using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ComplexNutrition.Modules.Tracking
{
    /// <summary>
    /// Server-side patches on vanilla's hunger behaviour, applied only while tracking runs:
    /// calories burn as satiety burned, fullness no longer burns with activity, starvation moves to the
    /// calorie reserve, the five category bars give no extra HP, and healing runs on the calorie reserve.
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
            var regen = AccessTools.Method(typeof(EntityBehaviorHealth), "ApplyRegenAndHunger");
            if (regen != null && SecondsRef != null)
            {
                harmony.Patch(regen, prefix: new HarmonyMethod(typeof(HungerPatches), nameof(RegenPrefix)));
            }
        }

        private static readonly AccessTools.FieldRef<EntityBehaviorHealth, float> SecondsRef =
            AccessTools.Field(typeof(EntityBehaviorHealth), "secondsSinceLastUpdate") == null ? null
            : AccessTools.FieldRefAccess<EntityBehaviorHealth, float>("secondsSinceLastUpdate");

        /// <summary>
        /// Vanilla heals at full speed only above 75% fullness, slower below, and pays for it in satiety. While
        /// tracking runs, a player heals at full speed whenever there is anything in the calorie reserve, and since v0.3.3
        /// each point healed costs <see cref="TrackingSettings.KcalPerHp"/> calories (more the worse the injury) in place of
        /// vanilla's satiety cost. Only this natural regeneration costs anything: healing items don't. With an empty reserve
        /// there is no healing.
        /// </summary>
        public static bool RegenPrefix(EntityBehaviorHealth __instance)
        {
            if (module == null || __instance.entity is not EntityPlayer ep) return true;
            if (ep.Player?.WorldData?.CurrentGameMode == EnumGameMode.Creative) return true;
            var player = ep.World.PlayerByUid(ep.PlayerUID) as IServerPlayer;
            var state = player == null ? null : module.StateOf(ep);
            if (state == null) return true;

            float health = __instance.Health, max = __instance.MaxHealth;
            if (health >= max) return false;
            if (state.GetReserve(Core.NutrientCodes.Calories) <= 0f) return false;

            float speed = StringUtil.ToFloat(ep.Api.World.Config.GetString("playerHealthRegenSpeed", "1"), 0f);
            // Since v0.4.0 vitamin K speeds up or slows down natural healing: only our own part of the healing stat counts here.
            // An empty vitamin K store only slows healing (to 90%); nothing in the effects can stop it, and if working out the
            // effect ever fails, healing goes on at the normal rate rather than not at all.
            float effect = 1f;
            try { effect = Math.Max(0.1f, 1f + EffectsRunner.OwnShare(state, "healingeffectivness")); }
            catch (Exception e) { LogOnce(ep, e); }
            float perSecond = 0.000333333f * speed * effect;
            var cal = ep.Api.World.Calendar;
            float gameSeconds = SecondsRef(__instance) * cal.SpeedOfTime * cal.CalendarSpeedMul;
            float healed = Math.Min(gameSeconds * perSecond, max - health);
            if (healed <= 0f) return false;
            // Priced at the middle of this step's health, so a big step costs what small ones would.
            module.BurnCalories(player, module.Settings.KcalPerHp((max - (health + healed / 2f)) / max) * healed);
            __instance.Health = health + healed;
            return false;
        }

        private static bool loggedRegenError;

        private static void LogOnce(EntityPlayer ep, Exception e)
        {
            if (loggedRegenError) return;
            loggedRegenError = true;
            ep.Api.Logger.Error("[complexnutrition] Working out the healing effect failed; healing at the normal rate instead: {0}", e);
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
        /// from an empty calorie reserve instead (see the tracking tick), so an empty stomach is kept just above zero.
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
