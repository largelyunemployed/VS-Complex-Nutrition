using System;
using System.Collections.Generic;
using ComplexNutrition.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace ComplexNutrition.Modules.Tracking
{
    /// <summary>
    /// Server: puts nutrient effects (v0.4.0) on players. Each effect is a modifier on a vanilla stat under our own name
    /// ("complexnutrition-vitaminA"), set only when it changes and never saved, so removing the mod leaves nothing behind.
    /// Vitamin D's resistance goes through the health behaviour's damage hook, as armor's does. Also keeps the timed
    /// nutrients' counters and makes vitamin D from sunlight.
    /// </summary>
    public sealed class EffectsRunner
    {
        private readonly TrackingModule module;
        private readonly ICoreServerAPI sapi;

        /// <summary>The modifiers each player has now, by "stat|nutrient", so a stat is only touched when its value changes.</summary>
        private readonly Dictionary<long, Dictionary<string, float>> applied = new();

        /// <summary>The damage hooks we added, by entity, to take off again.</summary>
        private readonly Dictionary<long, (EntityBehaviorHealth health, OnDamagedDelegate hook)> hooks = new();

        public EffectsRunner(TrackingModule module, ICoreServerAPI sapi)
        {
            this.module = module;
            this.sapi = sapi;
        }

        private static EffectSettings S => NutrientEffects.Settings;

        /// <summary>
        /// One tracking step for a player: the timed counters and sunlight over <paramref name="hours"/> of play (when the body
        /// is running: alive and in survival), then every effect brought up to date.
        /// </summary>
        public void Step(IServerPlayer player, NutritionState state, float hours, bool running)
        {
            if (running && hours > 0f)
            {
                foreach (var code in S.WithEffects)
                {
                    if (S.Of(code).Rule != EffectRules.Timed) continue;
                    state.SetHoursWithoutFullDay(code, NutrientEffects.NextHoursWithout(state.GetHoursWithoutFullDay(code), state.GetReserve(code), hours));
                }
                try { MakeVitaminD(player, state, hours); }
                catch (Exception e) { LogOnce("making vitamin D from sunlight", e); }
            }
            try { Apply(player, state, running); }
            catch (Exception e) { LogOnce("applying nutrient effects", e); }
        }

        private readonly HashSet<string> logged = new();

        /// <summary>Logs a failure once per kind, so one bad step can't stop the tracking tick or flood the log.</summary>
        private void LogOnce(string what, Exception e)
        {
            if (!logged.Add(what)) return;
            sapi.Logger.Error("[complexnutrition] Failed {0}: {1}", what, e);
        }

        /// <summary>Starts the timed counters (vitamin C and the B vitamins) again, for a respawn with Keep vitamin and mineral reserves on death switched off.</summary>
        public void Reset(NutritionState state)
        {
            foreach (var code in S.WithEffects)
            {
                if (S.Of(code).Rule == EffectRules.Timed) state.SetHoursWithoutFullDay(code, 0f);
            }
        }

        /// <summary>Sunlight on the player's head makes vitamin D: open sky times how high the sun stands, up to a day's limit.</summary>
        private void MakeVitaminD(IServerPlayer player, NutritionState state, float hours)
        {
            var n = module.Core.Nutrients.Get(S.SunNutrient);
            if (n == null) return;
            var entity = player.Entity;
            var world = sapi.World;
            int today = (int)Math.Floor(world.Calendar.TotalDays);
            if (state.SunDay != today)
            {
                state.SunDay = today;
                state.SunMadeToday = 0f;
            }
            float exposure = Exposure(entity);
            float made = NutrientEffects.SunMade(S, exposure, hours, state.SunMadeToday);
            if (made <= 0f) return;
            state.SunMadeToday += made;
            float old = state.GetReserve(n.Code);
            float now = Math.Min(n.CapFor(state), old + made);
            if (now == old) return;
            state.SetReserve(n.Code, now);
            module.Core.PublishReserveChanged(player, n.Code, old, now);
        }

        /// <summary>How much sun reaches the player, 0 to 1: the sunlight at their head (none indoors or underground) times the sun's height.</summary>
        public float Exposure(Entity entity)
        {
            var world = sapi.World;
            var pos = entity.Pos;
            var head = new BlockPos((int)Math.Floor(pos.X), (int)Math.Floor(pos.Y + entity.LocalEyePos.Y), (int)Math.Floor(pos.Z), pos.Dimension);
            float sky = world.BlockAccessor.GetLightLevel(head, EnumLightLevelType.OnlySunLight) / (float)Math.Max(1, world.SunBrightness);
            var sun = world.Calendar.GetSunPosition(pos.XYZ, world.Calendar.TotalDays);
            float height = sun == null ? 0f : Math.Clamp(sun.Y, 0f, 1f);
            return Math.Clamp(sky, 0f, 1f) * height;
        }

        /// <summary>Brings every stat modifier up to date (all taken off when the body isn't running or effects are off).</summary>
        public void Apply(IServerPlayer player, NutritionState state, bool running)
        {
            var entity = player.Entity;
            if (entity == null) return;
            EnsureHook(entity);
            var health = entity.GetBehavior<EntityBehaviorHealth>();
            float baseHealth = health?.BaseMaxHealth ?? 15f;
            if (!applied.TryGetValue(entity.EntityId, out var mine)) applied[entity.EntityId] = mine = new Dictionary<string, float>();
            bool healthChanged = false;
            bool armorChanged = false;

            // Puts one modifier (named after its source, a nutrient code or the diet tier) on a stat, or takes it off at zero.
            void SetModifier(EffectTarget target, string source, float delta)
            {
                string key = target.Stat + "|" + source;
                bool had = mine.TryGetValue(key, out float last);
                if (Math.Abs(delta) < 0.0005f)
                {
                    if (!had) return;
                    entity.Stats.Remove(target.Stat, NutrientEffects.StatPrefix + source);
                    mine.Remove(key);
                }
                else
                {
                    // Shares move in steps of 0.1% and health in 0.05 points, so a slowly draining store doesn't resend the stats every few seconds.
                    float step = target.PerBaseHealth ? 0.05f : 0.001f;
                    if (had && Math.Abs(delta - last) < step) return;
                    entity.Stats.Set(target.Stat, NutrientEffects.StatPrefix + source, delta, false);
                    mine[key] = delta;
                }
                if (target.PerBaseHealth) healthChanged = true;
                if (target.Stat == ArmorSlowdownStat) armorChanged = true;
            }

            foreach (var code in S.WithEffects)
            {
                float strength = running ? NutrientEffects.Current(state, code) : 0f;
                foreach (var t in S.Of(code).Targets)
                {
                    if (string.IsNullOrEmpty(t.Stat) || t.Stat == NutrientEffects.PhysicalResistance) continue;
                    SetModifier(t, code, NutrientEffects.StatDelta(t, strength, baseHealth));
                }
            }

            // The diet tier's effects (since v0.4.3): every stat any tier changes, at the current tier's value or nothing.
            var tier = running && NutrientEffects.WorldOn ? DietScore.Settings.Tier(state.DietTier) : null;
            foreach (var target in DietScore.TierStats(DietScore.Settings))
            {
                var current = tier?.Effects.Find(e => e.Stat == target.Stat);
                SetModifier(target, DietScore.TierSource, current == null ? 0f : NutrientEffects.StatDelta(current, current.Value, baseHealth));
            }
            if (healthChanged) health?.UpdateMaxHealth();
            if (armorChanged) RefreshArmorSlowdown(player);
        }

        /// <summary>The vanilla stat for how much worn armor slows you down (vitamin E's effect since 0.4.2d).</summary>
        private const string ArmorSlowdownStat = "armorWalkSpeedAffectedness";

        /// <summary>The game's own method that works out walk speed from worn armor. It is private, so it is found once by name.</summary>
        private static readonly System.Reflection.MethodInfo UpdateWearableStats =
            HarmonyLib.AccessTools.Method(typeof(ModSystemWearableStats), "updateWearableStats");

        /// <summary>
        /// The game only works out armor's slowdown when a clothing slot changes or the player joins. When vitamin E's effect
        /// changes, this asks it to work it out again, so the new slowdown applies straight away.
        /// </summary>
        private void RefreshArmorSlowdown(IServerPlayer player)
        {
            try
            {
                var wearableStats = player.Entity?.Api?.ModLoader.GetModSystem<ModSystemWearableStats>();
                var characterInventory = player.InventoryManager?.GetOwnInventory(GlobalConstants.characterInvClassName);
                if (wearableStats == null || characterInventory == null || UpdateWearableStats == null) return;
                UpdateWearableStats.Invoke(wearableStats, new object[] { characterInventory, player });
            }
            catch (Exception e)
            {
                LogOnce("armor slowdown", e);
            }
        }

        /// <summary>Takes every modifier and hook off a player (tracking switched off, or the player leaving).</summary>
        public void Clear(IServerPlayer player)
        {
            var entity = player?.Entity;
            if (entity == null) return;
            if (applied.TryGetValue(entity.EntityId, out var mine))
            {
                bool healthChanged = false;
                bool armorChanged = false;
                foreach (var key in mine.Keys)
                {
                    int bar = key.IndexOf('|');
                    entity.Stats.Remove(key.Substring(0, bar), NutrientEffects.StatPrefix + key.Substring(bar + 1));
                    if (key.StartsWith("maxhealthExtraPoints|")) healthChanged = true;
                    if (key.StartsWith(ArmorSlowdownStat + "|")) armorChanged = true;
                }
                applied.Remove(entity.EntityId);
                if (healthChanged) entity.GetBehavior<EntityBehaviorHealth>()?.UpdateMaxHealth();
                if (armorChanged) RefreshArmorSlowdown(player);
            }
            if (hooks.TryGetValue(entity.EntityId, out var h))
            {
                h.health.onDamaged -= h.hook;
                hooks.Remove(entity.EntityId);
            }
        }

        private void EnsureHook(Entity entity)
        {
            if (hooks.TryGetValue(entity.EntityId, out var existing) && existing.health == entity.GetBehavior<EntityBehaviorHealth>()) return;
            var health = entity.GetBehavior<EntityBehaviorHealth>();
            if (health == null) return;
            OnDamagedDelegate hook = (damage, source) => Resist(entity, damage, source);
            health.onDamaged += hook;
            hooks[entity.EntityId] = (health, hook);
        }

        /// <summary>Blunt, slashing and piercing damage, cut (or raised) by the effects that target physical resistance.</summary>
        private float Resist(Entity entity, float damage, DamageSource source)
        {
            if (!module.IsEnabled || source == null) return damage;
            if (entity is EntityPlayer ep && ep.Player?.WorldData?.CurrentGameMode != EnumGameMode.Survival) return damage;
            var state = module.StateOf(entity);
            if (state == null) return damage;
            float strength = PhysicalResistance(state);
            return strength == 0f ? damage : NutrientEffects.ResistDamage(damage, source.Type, strength);
        }

        /// <summary>The sum of every effect on physical resistance, as a share.</summary>
        public static float PhysicalResistance(NutritionState state)
        {
            float sum = 0f;
            foreach (var code in S.WithEffects)
            {
                foreach (var t in S.Of(code).Targets)
                {
                    if (t.Stat == NutrientEffects.PhysicalResistance) sum += NutrientEffects.Current(state, code);
                }
            }
            return sum;
        }

        /// <summary>
        /// Our own share of a stat (the sum of our modifiers on it): natural healing listens only to this part of the healing stat,
        /// so armor that slows bandages doesn't slow natural healing as well.
        /// </summary>
        public static float OwnShare(NutritionState state, string stat)
        {
            float sum = 0f;
            foreach (var code in S.WithEffects)
            {
                float strength = NutrientEffects.Current(state, code);
                foreach (var t in S.Of(code).Targets)
                {
                    if (t.Stat == stat) sum += NutrientEffects.StatDelta(t, strength, 0f);
                }
            }
            sum += DietScore.TierEffect(state, stat);
            return sum;
        }
    }
}
