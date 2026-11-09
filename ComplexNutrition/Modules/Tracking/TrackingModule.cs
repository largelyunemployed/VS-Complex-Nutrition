using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ComplexNutrition.Core;
using ComplexNutrition.UI;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace ComplexNutrition.Modules.Tracking
{
    /// <summary>
    /// v0.2 module: gives each player a reserve per nutrient that fills when they eat and drains over time.
    /// While it runs, the five vanilla nutrition bars and their +12.5 HP bonus are replaced by our reserves;
    /// switching it off brings both back.
    ///
    /// Since v0.2.2 it also runs digestion (see <see cref="DigestionSettings"/>): vanilla satiety becomes
    /// fullness (max 600, filled by each food's weight, draining at a fixed rate), eaten fats, protein and net
    /// carbohydrate go into gut pools that are absorbed over hours, and only absorption fills the calorie
    /// reserve. Activity burns the reserve as it burned satiety, and an empty reserve starves.
    /// </summary>
    public class TrackingModule : INutritionModule
    {
        public const string ModuleId = ModuleIds.Tracking;

        public string Id => ModuleId;
        public string[] Requires => new string[0];
        public bool EnabledByDefault => true;
        public bool IsEnabled { get; private set; }

        public ComplexNutritionModSystem Core { get; private set; }
        public TrackingSettings Settings { get; private set; } = new();
        public DigestionSettings Digest { get; private set; } = new();

        /// <summary>Fullness kept in an "empty" stomach, so vanilla's own starvation check never fires.</summary>
        public const float EmptyStomach = 0.001f;

        /// <summary>Server: set while the module runs.</summary>
        private ICoreServerAPI sapi;
        /// <summary>Server: set from <see cref="Start"/>, whether the module runs or not (commands, the join-while-off conversion).</summary>
        private ICoreServerAPI serverApi;
        private long tickListener;
        private float starveTimer;
        private float heldTimer;
        /// <summary>Client: the running-low icon right of the hotbar.</summary>
        private RunningLowIcon lowIcon;
        /// <summary>Server: puts nutrient effects on players (v0.4.0).</summary>
        internal EffectsRunner Effects { get; private set; }

        public void Start(ICoreAPI api, ComplexNutritionModSystem core)
        {
            Core = core;
            if (api is not ICoreServerAPI server) return;
            serverApi = server;
            RegisterCommands();
            server.Event.PlayerNowPlaying += OnJoinWhileOff;
        }

        public void Enable(ICoreAPI api, ComplexNutritionModSystem core)
        {
            Core = core;
            Settings = TrackingSettings.Load(api);
            Digest = DigestionSettings.Load(api);
            Digestion.Settings = Digest;
            IsEnabled = true;

            // The fats, protein and carbohydrate bars show the gut pools, so their cap is the pool size (digestion.json is the one source).
            foreach (var kv in Digest.Pools)
            {
                var def = core.Nutrients.Get(kv.Key);
                if (def == null) api.Logger.Warning("[complexnutrition] Gut pool {0} has no nutrient in nutrients.json; it will hold nothing.", kv.Key);
                else if (def.DailyTarget > 0) def.Cap = kv.Value.MaxGrams / def.DailyTarget * 100f;
            }
            var protein = core.Nutrients.Get(NutrientCodes.Protein);
            if (protein != null && Digest.Pool(NutrientCodes.Protein) != null && Math.Abs(protein.DailyTarget - Digest.ProteinNeedGrams) > 0.01f)
            {
                api.Logger.Warning("[complexnutrition] Protein's dailyTarget in nutrients.json ({0}g) differs from the protein pool's diminishingAboveGrams in digestion.json ({1}g). Bars and tooltips measure against the first, calories against the second.",
                    protein.DailyTarget, Digest.ProteinNeedGrams);
            }

            if (api is ICoreServerAPI serverApi)
            {
                sapi = serverApi;
                core.RequireEatingHooks();
                core.FoodEaten += OnFoodEaten;
                HungerPatches.Apply(this);
                sapi.Event.PlayerJoin += InitPlayer;
                sapi.Event.PlayerNowPlaying += InitPlayer;
                sapi.Event.PlayerRespawn += OnRespawn;
                sapi.Event.PlayerDisconnect += OnLeave;
                Effects = new EffectsRunner(this, sapi);
                core.HistoryProvider = BuildHistory;
                tickListener = sapi.Event.RegisterGameTickListener(OnTick, (int)(Math.Max(0.5f, Settings.UpdateSeconds) * 1000));
                foreach (IServerPlayer player in sapi.World.AllOnlinePlayers) InitPlayer(player);
            }
            else if (api is ICoreClientAPI capi)
            {
                CharacterStatsPatch.Apply(capi, core);
                FullnessDisplayPatch.Apply(core);
                lowIcon = new RunningLowIcon(capi, core);
                lowIcon.Register();
            }
        }

        public void Disable(ICoreAPI api)
        {
            if (!IsEnabled) return;
            IsEnabled = false;
            if (sapi != null)
            {
                Core.FoodEaten -= OnFoodEaten;
                sapi.Event.PlayerJoin -= InitPlayer;
                sapi.Event.PlayerNowPlaying -= InitPlayer;
                sapi.Event.PlayerRespawn -= OnRespawn;
                sapi.Event.PlayerDisconnect -= OnLeave;
                if (Core.HistoryProvider == BuildHistory) Core.HistoryProvider = null;
                sapi.Event.UnregisterGameTickListener(tickListener);
                HungerPatches.Remove();
                foreach (IServerPlayer player in sapi.World.AllOnlinePlayers)
                {
                    var state = NutritionState.Of(player.Entity);
                    if (state == null) continue;
                    Effects?.Clear(player);
                    // Only the flag: if tracking stays off, the player is converted to vanilla values when they next join.
                    state.TrackingOn = false;
                    state.MarkDirty();
                    RefreshVanillaHealthBonus(player);
                }
                sapi = null;
            }
            else
            {
                CharacterStatsPatch.Remove();
                FullnessDisplayPatch.Remove();
                lowIcon?.Unregister();
                lowIcon?.Dispose();
                lowIcon = null;
            }
            Digestion.Settings = null;
        }

        /// <summary>
        /// Switched off while the game runs: every online player goes back to vanilla values worked out from ours (ours are
        /// kept), as joining a world with tracking off does, so switching off cannot be used to dodge starvation.
        /// Switching tracking back on puts them back exactly where they were.
        /// </summary>
        public void OnSwitchedOff(ICoreAPI api)
        {
            if (serverApi == null) return;
            var digest = DigestionSettings.Load(serverApi);
            foreach (IServerPlayer p in serverApi.World.AllOnlinePlayers) ConvertToVanilla(p, digest);
        }

        /// <summary>A player joins while tracking is off: if they were tracked before, they get vanilla values from ours.</summary>
        private void OnJoinWhileOff(IServerPlayer player)
        {
            if (IsEnabled || NutritionState.Of(player?.Entity) == null) return;
            ConvertToVanilla(player, DigestionSettings.Load(serverApi));
        }

        private void ConvertToVanilla(IServerPlayer player, DigestionSettings digest)
        {
            var state = NutritionState.Of(player?.Entity);
            if (state == null) return;
            state.TrackingOn = false;   // tells the client to show vanilla's bars
            Digestion.ConvertToVanilla(player.Entity, Core.Nutrients, digest);
            state.MarkDirty();
        }

        private double NowHours => sapi.World.Calendar.TotalHours;
        private int Today => (int)Math.Floor(sapi.World.Calendar.TotalDays);

        /// <summary>Starts tracking a player (new or returning). Time spent offline does not drain reserves.</summary>
        private void InitPlayer(IServerPlayer player)
        {
            if (player?.Entity == null) return;
            var state = NutritionState.GetOrCreate(player.Entity, Core.Nutrients, NowHours, out bool created);
            if (created) ApplyStartCondition(player, state);
            MigrateGut(state);
            FitStores(state);
            state.TrackingOn = true;
            state.LastUpdateHours = NowHours;
            Digestion.UseOurScale(player.Entity, Digest);
            if (created)
            {
                // A new character starts with an empty stomach, whatever the start condition.
                var hunger = player.Entity.GetBehavior<EntityBehaviorHunger>();
                if (hunger != null) hunger.Saturation = EmptyStomach;
            }
            EnsureDay(player, state);
            Effects?.Apply(player, state, Runs(player));
            state.MarkDirty();
            RefreshVanillaHealthBonus(player);
        }

        /// <summary>The player leaves: forget the effects we put on them (they aren't saved, so nothing stays behind).</summary>
        private void OnLeave(IServerPlayer player) => Effects?.Clear(player);

        /// <summary>Whether a player's body is running: alive and not in creative or spectator mode. Only then do reserves drain and effects apply.</summary>
        private static bool Runs(IServerPlayer player)
        {
            var mode = player.WorldData.CurrentGameMode;
            return player.Entity.Alive && mode != EnumGameMode.Creative && mode != EnumGameMode.Spectator;
        }

        /// <summary>Since v0.4.0 the slow stores hold 20 days (A, D, E and K held 25): anything over a store's cap is trimmed to it.</summary>
        private void FitStores(NutritionState state)
        {
            foreach (var n in Core.Nutrients.All)
            {
                if (!n.IsMicronutrient) continue;
                float r = state.GetReserve(n.Code);
                float cap = n.CapFor(state);
                if (r > cap) state.SetReserve(n.Code, cap);
            }
        }

        /// <summary>
        /// A character new to Complex Nutrition starts with what the world's two start settings pick (since v0.4.2): Starting
        /// calories fills that share of the calorie reserve, and Starting vitamin and mineral reserves that share of every
        /// vitamin, mineral and fiber store (Full 100%, Easy 70%, Normal 50%, Hard 30%, Brutal 10%, Rock Bottom 0.1%).
        /// Only on first join. A world from before v0.4.2 has no stores setting, and its stores follow Starting calories as
        /// before; a world from before v0.2.4 has neither, and the nutrients' startReserve applies.
        /// </summary>
        private void ApplyStartCondition(IServerPlayer player, NutritionState state)
        {
            float? caloriesChoice = WorldSettings.StartCalories(sapi.World);
            float? storesChoice = WorldSettings.StartStores(sapi.World);
            if (caloriesChoice != null) FillCalories(player, state, WorldSettings.StoreShare(caloriesChoice.Value), publish: false);
            if (storesChoice != null) FillStores(player, state, WorldSettings.StoreShare(storesChoice.Value), publish: false);
        }

        /// <summary>Sets the calorie reserve to <paramref name="shareOfReserve"/> (0 to 1) of its size for this player.</summary>
        private void FillCalories(IServerPlayer player, NutritionState state, float shareOfReserve, bool publish)
        {
            var calories = Core.Nutrients.Get(NutrientCodes.Calories);
            if (calories != null) SetReserveTo(player, state, calories, calories.CapFor(state) * shareOfReserve, publish);
        }

        /// <summary>
        /// Sets every vitamin and mineral store, and the fiber store, to <paramref name="shareOfStore"/> (0 to 1) of its size:
        /// Normal (0.5) gives 10 days of each 20-day store and 1 day of the 2-day ones (vitamin C, the B vitamins and fiber).
        /// </summary>
        private void FillStores(IServerPlayer player, NutritionState state, float shareOfStore, bool publish)
        {
            foreach (var nutrient in Core.Nutrients.All)
            {
                if (nutrient.IsMicronutrient || nutrient.Code == NutrientCodes.Fiber)
                    SetReserveTo(player, state, nutrient, nutrient.CapFor(state) * shareOfStore, publish);
            }
        }

        /// <summary>Sets one reserve (in % of a day) and, when <paramref name="publish"/> is on, tells other mods it changed.</summary>
        private void SetReserveTo(IServerPlayer player, NutritionState state, NutrientDefinition nutrient, float value, bool publish)
        {
            float old = state.GetReserve(nutrient.Code);
            state.SetReserve(nutrient.Code, value);
            if (publish && old != value) Core.PublishReserveChanged(player, nutrient.Code, old, value);
        }

        /// <summary>Saves from v0.2.2 to v0.3.0 kept the gut as batches as well as in the reserves; since v0.3.1 the reserve is the one record.</summary>
        private void MigrateGut(NutritionState state)
        {
            foreach (var kv in Digest.Pools)
            {
                float? grams = state.TakeOldGutBatches(kv.Key);
                var def = Core.Nutrients.Get(kv.Key);
                if (grams == null || def == null) continue;
                state.SetReserve(def.Code, def.ToPercent(Math.Min(kv.Value.MaxGrams, grams.Value)));
            }
        }

        /// <summary>
        /// A character respawns after dying (reworked in v0.4.2 to be gentler). Three things happen:
        /// 1. The calorie reserve follows Respawn with calories (<see cref="SetRespawnCalories"/>).
        /// 2. With Keep vitamin and mineral reserves on death switched on (the default), the vitamin, mineral and fiber stores
        ///    and the vitamin C and B vitamin day counts stay as they were. Switched off, the stores go back to what a new
        ///    character starts with (Starting vitamin and mineral reserves) and the day counts start again.
        /// 3. The gut always empties, with the last 24 hours of digestion, so the new body starts digesting from nothing.
        /// Vanilla gives half a stomach of fullness.
        /// </summary>
        private void OnRespawn(IServerPlayer player)
        {
            var state = player?.Entity == null ? null : StateOf(player.Entity);
            if (state == null) return;
            SetRespawnCalories(player, state);
            if (!WorldSettings.KeepStores(sapi.World))
            {
                float storesChoice = WorldSettings.StartStores(sapi.World) ?? WorldSettings.DefaultStartStoresValue;
                FillStores(player, state, WorldSettings.StoreShare(storesChoice), publish: true);
                Effects?.Reset(state);
            }
            EmptyGut(player, state);
            state.MarkDirty();
            // Trend arrows compare against the new body, not the old one.
            var history = NutrientHistory.Of(player.Entity);
            history.ClearHours();
            history.RecordHour(CurrentHour, Reserves(state));
        }

        /// <summary>
        /// Sets the calorie reserve of a character who just respawned, by the world's Respawn with calories choice:
        /// - Lose calories on death (the default): a tenth of what the reserve held (respawnKeptShare), but at least 1,000kcal
        ///   (respawnMinKcal), and never more than the reserve holds.
        /// - Keep calories on death: unchanged.
        /// - Reset calorie progress on death: 1,000kcal (respawnResetKcal), or Rock Bottom's share of the reserve on a world
        ///   that starts at Rock Bottom, and the diet goes back to the first tier.
        /// </summary>
        private void SetRespawnCalories(IServerPlayer player, NutritionState state)
        {
            var calories = Core.Nutrients.Get(NutrientCodes.Calories);
            if (calories == null) return;
            string choice = WorldSettings.RespawnCalories(sapi.World);
            // Reset first, so the reserve's size is the first tier's when the new amount is worked out.
            if (choice == WorldSettings.RespawnReset) state.ResetDietProgress();
            float atDeath = state.GetReserve(calories.Code);
            float afterRespawn = Settings.CaloriesOnRespawn(choice, atDeath, calories.CapFor(state), calories, WorldSettings.RockBottomStart(sapi.World));
            SetReserveTo(player, state, calories, afterRespawn, publish: true);
        }

        /// <summary>Empties the gut on death: carbs, fats, protein and fiber still being digested, and the 24-hour digestion logs.</summary>
        private void EmptyGut(IServerPlayer player, NutritionState state)
        {
            foreach (var kv in Digest.Pools)
            {
                var def = Core.Nutrients.Get(kv.Key);
                if (def != null)
                {
                    float old = state.GetReserve(def.Code);
                    SetGut(state, def, 0f, kv.Value, 1f);
                    if (old != 0f) Core.PublishReserveChanged(player, def.Code, old, 0f);
                }
                state.SetAbsorbedLog(kv.Key, new float[24]);
                state.SetAbsorbedLog(Digestion.KcalLogKey(kv.Key), new float[24]);
            }
            state.FiberGut = 0f;
            state.SetGutHours(NutrientCodes.Fiber, 0f);
            state.SetAbsorbedLog(NutrientCodes.Fiber, new float[24]);
            state.SetAbsorbedLog(Digestion.KcalLogKey(NutrientCodes.Fiber), new float[24]);
            state.Protein24 = 0f;
            state.ClearSitting();
        }

        /// <summary>Lets vanilla recompute its nutrition HP bonus; our patch keeps it at zero while tracking is on.</summary>
        private static void RefreshVanillaHealthBonus(IServerPlayer player)
        {
            player.Entity?.GetBehavior<EntityBehaviorHunger>()?.UpdateNutrientHealthBoost();
        }

        /// <summary>Server: the tracked state of an online player, or null when tracking is not running for them.</summary>
        public NutritionState StateOf(Vintagestory.API.Common.Entities.Entity entity)
        {
            var state = NutritionState.Of(entity);
            return state != null && state.TrackingOn ? state : null;
        }

        private void EnsureDay(IServerPlayer player, NutritionState state)
        {
            int today = Today;
            int day = state.Day;
            if (day == today) return;
            var ledger = DietLedger.Of(player.Entity);
            var history = NutrientHistory.Of(player.Entity);
            if (day != int.MinValue && day < today)
            {
                Core.PublishDailyRollover(player, ledger.Get(day));
            }
            state.ClearToday();
            state.Day = today;
            ledger.Prune(today, Settings.LedgerDays);
            history.Prune(today, Settings.LedgerDays);
        }

        private long CurrentHour => (long)Math.Floor(NowHours);

        /// <summary>Every nutrient's reserve, for the history.</summary>
        private Dictionary<string, float> Reserves(NutritionState state)
        {
            var result = new Dictionary<string, float>();
            foreach (var n in Core.Nutrients.All) result[n.Code] = state.GetReserve(n.Code);
            return result;
        }

        /// <summary>Once per game hour: the reserves, for the window's trend arrows.</summary>
        private void RecordHourly(IServerPlayer player, NutritionState state)
        {
            var history = NutrientHistory.Of(player.Entity);
            if (history.LastHour != CurrentHour) history.RecordHour(CurrentHour, Reserves(state));
        }

        /// <summary>The nutrition window's history for a player, or null when tracking is not running for them.</summary>
        private HistoryData BuildHistory(IServerPlayer player)
        {
            var state = player?.Entity == null || sapi == null ? null : StateOf(player.Entity);
            if (state == null) return null;
            return NutrientHistory.Of(player.Entity).Build(DietLedger.Of(player.Entity), Today, NowHours, Settings.LedgerDays);
        }

        private void OnTick(float dt)
        {
            starveTimer += dt;
            bool starveNow = starveTimer >= Digest.StarvationSeconds;
            if (starveNow) starveTimer = 0f;
            heldTimer += dt;
            bool scanHeld = heldTimer >= Settings.HeldScanSeconds;
            if (scanHeld) heldTimer = 0f;

            foreach (IServerPlayer player in sapi.World.AllOnlinePlayers)
            {
                if (player.ConnectionState != EnumClientState.Playing || player.Entity == null) continue;
                var state = StateOf(player.Entity);
                if (state == null)
                {
                    InitPlayer(player);
                    continue;
                }
                EnsureDay(player, state);
                RecordHourly(player, state);
                if (scanHeld) RecordHeld(player);

                bool drains = Runs(player);
                if (starveNow && drains && state.GetReserve(NutrientCodes.Calories) <= 0f)
                {
                    // Vanilla's starvation, moved from an empty stomach to an empty calorie reserve.
                    player.Entity.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Internal, Type = EnumDamageType.Hunger }, Digest.StarvationDamage);
                }

                double now = NowHours;
                double hours = now - state.LastUpdateHours;
                if (hours < 0)
                {
                    state.LastUpdateHours = now;   // time was set back
                    continue;
                }
                if (hours < Settings.MinDrainHours) continue;
                state.LastUpdateHours = now;

                if (drains)
                {
                    foreach (var n in Core.Nutrients.All)
                    {
                        if (n.IsCalories || Digest.Pool(n.Code) != null) continue;   // calories follow the hunger rate; gut pools digest below
                        float old = state.GetReserve(n.Code);
                        float now2 = Math.Max(0f, old - n.DrainPerDay * (float)(hours / 24.0));
                        state.SetReserve(n.Code, now2);
                        Core.PublishReserveChanged(player, n.Code, old, now2);
                    }

                    var hunger = player.Entity.GetBehavior<EntityBehaviorHunger>();
                    Digestion.FitStomach(player.Entity, Digest);
                    if (hunger != null)
                    {
                        float full = hunger.Saturation;
                        float next = Math.Max(EmptyStomach, full - Digest.FullnessDrainPerHour * (float)hours);
                        if (next != full) hunger.Saturation = next;
                    }

                    DigestStep(player, state, (float)hours, now);
                }
                Effects.Step(player, state, (float)hours, drains);
                state.MarkDirty();
            }
        }

        /// <summary>
        /// Remembers every food the player has held in their hotbar, backpack or mouse slot (and the drink in a held
        /// container), in WatchedAttributes["complexnutritionHeld"], so the nutrition window only suggests foods they know.
        /// Kept outside our state tree so it is only sent when something new is added.
        /// </summary>
        private void RecordHeld(IServerPlayer player)
        {
            var attrs = player.Entity.WatchedAttributes;
            var known = (attrs[NutritionState.HeldKey] as Vintagestory.API.Datastructures.StringArrayAttribute)?.value ?? new string[0];
            HashSet<string> added = null;

            void Consider(ItemStack stack, int depth)
            {
                var coll = stack?.Collectible;
                if (coll?.Code == null || depth > 1) return;
                if (coll is BlockLiquidContainerBase container)
                {
                    Consider(container.GetContent(stack), depth + 1);
                    return;
                }
                if (FoodDatabase.IsComposite(coll) || Core.Foods.Resolve(coll) == null) return;
                string code = coll.Code.ToString();
                if (Array.IndexOf(known, code) >= 0 || (added?.Contains(code) ?? false)) return;
                (added ??= new HashSet<string>()).Add(code);
            }

            foreach (var invName in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName, GlobalConstants.mousecursorInvClassName })
            {
                var inv = player.InventoryManager?.GetOwnInventory(invName);
                if (inv == null) continue;
                foreach (var slot in inv) Consider(slot?.Itemstack, 0);
            }
            if (added == null) return;
            attrs[NutritionState.HeldKey] = new Vintagestory.API.Datastructures.StringArrayAttribute(known.Concat(added).OrderBy(c => c).ToArray());
            attrs.MarkPathDirty(NutritionState.HeldKey);
        }

        /// <summary>Speed of digestion from the fibre reserve: 1 at an empty reserve, 1 + the pool's bonus at the full-bonus line or above.</summary>
        public float FiberSpeed(NutritionState state, DigestionPool pool) => Digestion.FiberSpeed(Digest, state, pool);

        /// <summary>Clears the hours of a 24-hour log that passed since <paramref name="lastHour"/>, ready for <paramref name="hour"/>.</summary>
        private static float[] Roll(float[] log, long hour, long lastHour)
        {
            if (lastHour == long.MinValue || hour - lastHour >= 24 || hour < lastHour) Array.Clear(log, 0, 24);
            else for (long h = lastHour + 1; h <= hour; h++) log[(int)(h % 24)] = 0f;
            return log;
        }

        /// <summary>
        /// One digestion step: each gut batch is absorbed at its own rate (faster with fibre), absorbed grams
        /// become calories after diminishing returns over each pool's rolling window, and the pools' bars,
        /// time-to-empty and the 24-hour protein total are updated.
        /// </summary>
        private void DigestStep(IServerPlayer player, NutritionState state, float hours, double now)
        {
            long hour = (long)Math.Floor(now);
            long lastHour = state.AbsorbedHour;
            float kcal = 0f;

            foreach (var kv in Digest.Pools)
            {
                string code = kv.Key;
                var pool = kv.Value;
                var log = Roll(state.GetAbsorbedLog(code), hour, lastHour);
                // The calories each hour's absorption gave after diminishing returns (v0.3.6, for the Overview's 24-hour readouts).
                var kcalLog = Roll(state.GetAbsorbedLog(Digestion.KcalLogKey(code)), hour, lastHour);

                // The pool absorbs as a whole, faster the more it holds, up to its fastest rate.
                var def = Core.Nutrients.Get(code);
                float speed = FiberSpeed(state, pool);
                float inGut = GutGrams(state, def);
                float left = pool.Absorb(inGut, hours, speed);
                float absorbed = inGut - left;
                if (def != null)
                {
                    float old = state.GetReserve(code);
                    float now2 = SetGut(state, def, left, pool, speed);
                    if (old != now2) Core.PublishReserveChanged(player, code, old, now2);
                }

                if (absorbed > 0f)
                {
                    float window = 0f;
                    int span = Math.Clamp(pool.WindowHours, 1, 24);
                    for (int k = 0; k < span; k++) window += log[(int)(((hour - k) % 24 + 24) % 24)];
                    float got = pool.Effective(window, absorbed) * pool.KcalPerGram;
                    kcal += got;
                    log[(int)(hour % 24)] += absorbed;
                    kcalLog[(int)(hour % 24)] += got;
                }
                state.SetAbsorbedLog(code, log);
                state.SetAbsorbedLog(Digestion.KcalLogKey(code), kcalLog);
                if (code == NutrientCodes.Protein)
                {
                    float total = 0f;
                    foreach (var g in log) total += g;
                    state.Protein24 = Math.Min(total, Digest.ProteinMaxGrams);   // the bar's top
                }
            }
            // Fiber's own 24-hour logs (v0.3.6): grams digested and the calories they gave.
            var fiberLog = Roll(state.GetAbsorbedLog(NutrientCodes.Fiber), hour, lastHour);
            var fiberKcalLog = Roll(state.GetAbsorbedLog(Digestion.KcalLogKey(NutrientCodes.Fiber)), hour, lastHour);
            float fiberKcal = DigestFiber(state, hours);
            if (fiberKcal > 0f)
            {
                fiberKcalLog[(int)(hour % 24)] += fiberKcal;
                fiberLog[(int)(hour % 24)] += fiberKcal / Math.Max(0.01f, Digest.FiberKcalPerGram);
            }
            state.SetAbsorbedLog(NutrientCodes.Fiber, fiberLog);
            state.SetAbsorbedLog(Digestion.KcalLogKey(NutrientCodes.Fiber), fiberKcalLog);
            kcal += fiberKcal;
            state.AbsorbedHour = hour;

            var cal = Core.Nutrients.Get(NutrientCodes.Calories);
            if (cal != null && kcal > 0f)
            {
                float old = state.GetReserve(cal.Code);
                float updated = Math.Max(old, Math.Min(cal.CapFor(state), old + cal.ToPercent(kcal)));   // a full reserve wastes the rest
                state.SetReserve(cal.Code, updated);
                state.AddAbsorbedToday(cal.Code, updated - old);
                NutrientHistory.Of(player.Entity).AddFlow(now, Today, cal.FromPercent(updated - old), 0f);
                Core.PublishReserveChanged(player, cal.Code, old, updated);
            }
        }

        /// <summary>Burns calories as vanilla burns satiety, so activity, cold and the hunger-rate stat all count.</summary>
        internal void BurnSatiety(IServerPlayer player, float satiety) => BurnCalories(player, satiety * Digest.KcalPerSatiety * Digest.ActivityBurnMultiplier);

        internal void BurnCalories(IServerPlayer player, float kcal)
        {
            var state = StateOf(player.Entity);
            var def = Core.Nutrients.Get(NutrientCodes.Calories);
            if (state == null || def == null || kcal <= 0f) return;
            float old = state.GetReserve(def.Code);
            float now = Math.Max(0f, old - def.ToPercent(kcal));
            state.SetReserve(def.Code, now);
            NutrientHistory.Of(player.Entity).AddFlow(NowHours, Today, 0f, def.FromPercent(old - now));
            state.MarkDirty();
            Core.PublishReserveChanged(player, def.Code, old, now);
        }

        /// <summary>
        /// Uses up the vitamin K that natural healing needs (since v0.4.1): <see cref="TrackingSettings.VitaminKPerHp"/> of a
        /// day's need for each of the <paramref name="healthHealed"/> points. The store never goes below empty.
        /// </summary>
        internal void UseVitaminKForHealing(IServerPlayer player, NutritionState state, float healthHealed)
        {
            var vitaminK = Core.Nutrients.Get(NutrientCodes.VitaminK);
            if (state == null || vitaminK == null || healthHealed <= 0f || Settings.VitaminKPerHp <= 0f) return;
            float before = state.GetReserve(vitaminK.Code);
            if (before <= 0f) return;
            float after = Math.Max(0f, before - Settings.VitaminKPerHp * healthHealed);
            state.SetReserve(vitaminK.Code, after);
            state.MarkDirty();
            Core.PublishReserveChanged(player, vitaminK.Code, before, after);
        }

        /// <summary>Absorption above the sitting limit: the extra x adds scale * ln(1 + x / scale).</summary>
        public float SittingCurve(float total, float limit)
        {
            if (total <= limit || !DigestionPool.DiminishingReturns) return total;
            float scale = Math.Max(1f, Settings.DiminishingScale);
            return limit + scale * (float)Math.Log(1.0 + (total - limit) / scale);
        }

        private void OnFoodEaten(FoodEatenInfo eaten)
        {
            var player = eaten.Player;
            var state = player?.Entity == null ? null : StateOf(player.Entity);
            if (state == null) return;
            EnsureDay(player, state);

            double now = NowHours;
            if (now - state.LastMealHours > Settings.SittingHours) state.ClearSitting();
            state.LastMealHours = now;

            var absorbed = new FoodAbsorbedInfo { Eaten = eaten };
            foreach (var n in Core.Nutrients.All)
            {
                // Calories come from digestion only, never straight from eating (fiber's too, since v0.3.5).
                if (n.IsCalories) continue;
                var pool = Digest.Pool(n.Code);
                if (pool != null)
                {
                    AddToGut(player, state, n, pool, DigestionSettings.PoolGrams(n.Code, eaten.Get), absorbed);
                    continue;
                }

                float amount = eaten.Get(n.Code);
                if (amount <= 0f) continue;
                float percent = n.ToPercent(amount);

                var multipliers = Core.AbsorptionMultipliers(player, n.Code);
                foreach (var m in multipliers.Values) percent *= m;
                if (multipliers.Count > 0) absorbed.Multipliers[n.Code] = multipliers;

                float before = state.GetSitting(n.Code);
                float after = before + percent;
                float gained = SittingCurve(after, n.SittingLimit) - SittingCurve(before, n.SittingLimit);
                state.SetSitting(n.Code, after);

                float old = state.GetReserve(n.Code);
                float updated = Math.Max(old, Math.Min(n.CapFor(state), old + gained));   // never cut a reserve already over the cap
                state.SetReserve(n.Code, updated);
                state.AddAbsorbedToday(n.Code, gained);
                absorbed.Absorbed[n.Code] = gained;
                Core.PublishReserveChanged(player, n.Code, old, updated);
            }

            AddFiberToGut(player, state, eaten.Get(NutrientCodes.Fiber));
            DietLedger.Of(player.Entity).Add(Today, eaten.KcalByCategory);
            state.MarkDirty();
            Core.PublishFoodAbsorbed(absorbed);
        }

        /// <summary>
        /// Fiber's calories (v0.3.4; digested since v0.3.5): eaten fiber also goes into the gut, up to fiberGutMaxGrams, where it is
        /// digested at the fiber reserve's own steady rate and gives fiberKcalPerGram calories a gram, with no diminishing returns.
        /// The fiber reserve fills as before.
        /// </summary>
        private void AddFiberToGut(IServerPlayer player, NutritionState state, float grams)
        {
            if (grams <= 0f || Digest.FiberKcalPerGram <= 0f) return;
            foreach (var m in Core.AbsorptionMultipliers(player, NutrientCodes.Fiber).Values) grams *= m;
            float rate = DigestionSettings.FiberGutRate(Core.Nutrients.Get(NutrientCodes.Fiber));
            if (grams <= 0f || rate <= 0f) return;
            float now = Math.Max(state.FiberGut, Math.Min(Digest.FiberGutMaxGrams, state.FiberGut + grams));
            state.FiberGut = now;
            state.SetGutHours(NutrientCodes.Fiber, now / rate);
        }

        /// <summary>Digests the fiber in the gut for <paramref name="hours"/>; returns the calories it gave.</summary>
        private float DigestFiber(NutritionState state, float hours)
        {
            float inGut = state.FiberGut;
            float rate = DigestionSettings.FiberGutRate(Core.Nutrients.Get(NutrientCodes.Fiber));
            if (inGut <= 0f || rate <= 0f) return 0f;
            float left = Math.Max(0f, inGut - rate * hours);
            if (left <= DigestionPool.EmptyGrams) left = 0f;
            state.FiberGut = left;
            state.SetGutHours(NutrientCodes.Fiber, left / rate);
            return (inGut - left) * Digest.FiberKcalPerGram;
        }

        /// <summary>Grams in a gut pool: its reserve, in grams.</summary>
        private static float GutGrams(NutritionState state, NutrientDefinition def) => def == null ? 0f : def.FromPercent(state.GetReserve(def.Code));

        /// <summary>Stores a gut pool's contents (its reserve) and how long it will take to empty. Returns the new reserve, in %.</summary>
        private static float SetGut(NutritionState state, NutrientDefinition def, float grams, DigestionPool pool, float speed)
        {
            bool any = grams > DigestionPool.EmptyGrams;
            float percent = any ? def.ToPercent(grams) : 0f;
            state.SetReserve(def.Code, percent);
            state.SetGutHours(def.Code, any ? pool.HoursToEmpty(grams, speed) : 0f);
            return percent;
        }

        /// <summary>
        /// Puts eaten grams in a gut pool, which absorbs them along with whatever it already holds.
        /// What does not fit in the pool passes through unused.
        /// </summary>
        private void AddToGut(IServerPlayer player, NutritionState state, NutrientDefinition n, DigestionPool pool, float grams, FoodAbsorbedInfo absorbed)
        {
            var multipliers = Core.AbsorptionMultipliers(player, n.Code);
            foreach (var m in multipliers.Values) grams *= m;
            if (multipliers.Count > 0) absorbed.Multipliers[n.Code] = multipliers;
            if (grams <= 0f) return;

            float inGut = GutGrams(state, n);
            float accepted = Math.Min(grams, Math.Max(0f, pool.MaxGrams - inGut));
            if (accepted <= 0.001f) return;
            float old = state.GetReserve(n.Code);
            float updated = SetGut(state, n, inGut + accepted, pool, FiberSpeed(state, pool));
            float percent = n.ToPercent(accepted);
            state.AddAbsorbedToday(n.Code, percent);
            absorbed.Absorbed[n.Code] = percent;
            Core.PublishReserveChanged(player, n.Code, old, updated);
        }

        // ---- Commands ----

        private void RegisterCommands()
        {
            var sapi = serverApi;
            var parsers = sapi.ChatCommands.Parsers;

            sapi.ChatCommands.GetOrCreate("cnutrition")
                .BeginSubCommand("stats")
                .WithDescription("Show your nutrient reserves")
                .RequiresPlayer()
                .RequiresPrivilege(Privilege.chat)
                .HandleWith(args =>
                {
                    if (!IsEnabled) return TextCommandResult.Error(Lang.Get("complexnutrition:cmd-tracking-off"));
                    var state = StateOf(args.Caller.Entity);
                    return state == null
                        ? TextCommandResult.Error(Lang.Get("complexnutrition:cmd-no-state"))
                        : TextCommandResult.Success(ReserveReport(state, false, args.Caller.Entity));
                })
                .EndSubCommand()
                .BeginSubCommand("display")
                .WithDescription("Choose how much detail nutrition tooltips show: advanced, simple, or world (follow the world setting)")
                .RequiresPlayer()
                .RequiresPrivilege(Privilege.chat)
                .WithArgs(parsers.OptionalWordRange("mode", "advanced", "simple", "world"))
                .HandleWith(args =>
                {
                    var state = NutritionState.Of(args.Caller.Entity);
                    if (state == null) return TextCommandResult.Error(Lang.Get("complexnutrition:cmd-no-state"));
                    string mode = args.Parsers[0].IsMissing ? null : (string)args[0];
                    if (mode != null)
                    {
                        state.DisplayChoice = mode == "advanced" ? 1 : mode == "simple" ? 2 : 0;
                        state.MarkDirty();
                    }
                    bool on = Digestion.AdvancedDisplay(sapi.World, state);
                    string which = state.DisplayChoice == 0 ? Lang.Get("complexnutrition:display-world") : Lang.Get("complexnutrition:display-own");
                    return TextCommandResult.Success(Lang.Get(on ? "complexnutrition:display-on" : "complexnutrition:display-off", which));
                })
                .EndSubCommand()
                .BeginSubCommand("reserves")
                    .WithDescription("Show a player's nutrient reserves and today's intake")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.OnlinePlayer("player"))
                    .HandleWith(args =>
                    {
                        var target = (IServerPlayer)args[0];
                        var state = StateOf(target.Entity);
                        return state == null ? TextCommandResult.Error("Tracking is not running for " + target.PlayerName)
                            : TextCommandResult.Success(target.PlayerName + ":\n" + ReserveReport(state, true, target.Entity));
                    })
                .EndSubCommand()
                .BeginSubCommand("setreserve")
                    .WithDescription("Set a player's reserve, in % of a day's need: /cnutrition setreserve <player> <nutrient code or all> <percent>")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.OnlinePlayer("player"), parsers.Word("nutrient"), parsers.Float("percent"))
                    .HandleWith(args => SetReserveCommand((IServerPlayer)args[0], (string)args[1], (float)args[2]))
                .EndSubCommand()
                .BeginSubCommand("setdayslow")
                    .WithDescription("For testing timed effects: set how many days a player's vitamin C or B vitamins have gone without a full day stored: /cnutrition setdayslow <player> <vitaminC|vitaminB|all> <days>")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.OnlinePlayer("player"), parsers.Word("nutrient"), parsers.Float("days"))
                    .HandleWith(args => SetDaysLowCommand((IServerPlayer)args[0], (string)args[1], (float)args[2]))
                .EndSubCommand()
                .BeginSubCommand("ledger")
                    .WithDescription("Show a player's diet ledger: calories per food category for each recorded day")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.OnlinePlayer("player"))
                    .HandleWith(args => LedgerCommand((IServerPlayer)args[0]))
                .EndSubCommand();
        }

        private string ReserveReport(NutritionState state, bool withToday = false, Vintagestory.API.Common.Entities.Entity entity = null)
        {
            var sb = new StringBuilder();
            var hunger = entity?.GetBehavior<EntityBehaviorHunger>();
            if (hunger != null) sb.AppendLine(Lang.Get("complexnutrition:fullness-hover", (int)Math.Round(hunger.Saturation), (int)Math.Round(hunger.MaxSaturation)));
            foreach (var n in Core.Nutrients.All)
            {
                float r = state.GetReserve(n.Code);
                string line = Digest.Pool(n.Code) != null
                    ? Lang.Get("complexnutrition:cmd-gut-line", Lang.Get(n.LangKey), n.Format(r, Core.ShowPercent), NutrientDisplay.GutHoursText(state.GetGutHours(n.Code), r))
                    : Lang.Get("complexnutrition:cmd-reserve-line", Lang.Get(n.LangKey), n.Format(r, Core.ShowPercent), NutrientDisplay.DaysText(r));
                if (withToday) line += $" (today +{Math.Round(state.GetAbsorbedToday(n.Code))}%, cap {Math.Round(n.CapFor(state))}%)";
                sb.AppendLine(line);
                string effect = NutrientDisplay.EffectNow(n.Code, state, entity?.GetBehavior<EntityBehaviorHealth>()?.BaseMaxHealth ?? 15f);
                if (effect != "") sb.AppendLine(Lang.Get("complexnutrition:cmd-effect-line", effect));
                if (n.Code == NutrientCodes.Protein) sb.AppendLine(Lang.Get("complexnutrition:protein24-hover", n.FormatAmount(state.Protein24, Core.ShowPercent), n.FormatAmount(Digest.ProteinNeedGrams, Core.ShowPercent), n.FormatAmount(Digest.ProteinMaxGrams, Core.ShowPercent)));
            }
            return sb.ToString().TrimEnd();
        }

        private TextCommandResult SetReserveCommand(IServerPlayer target, string code, float percent)
        {
            var state = StateOf(target.Entity);
            if (state == null) return TextCommandResult.Error("Tracking is not running for " + target.PlayerName);
            var targets = code == "all" ? Core.Nutrients.All.ToList() : new List<NutrientDefinition> { Core.Nutrients.Get(code) };
            if (targets.Any(t => t == null))
                return TextCommandResult.Error("Unknown nutrient " + code + ". Use one of: all, " + string.Join(", ", Core.Nutrients.All.Select(n => n.Code)));
            foreach (var n in targets)
            {
                float old = state.GetReserve(n.Code);
                float value = Math.Max(0f, percent);
                var pool = Digest.Pool(n.Code);
                if (pool != null) value = SetGut(state, n, Math.Min(pool.MaxGrams, n.FromPercent(value)), pool, FiberSpeed(state, pool));
                else state.SetReserve(n.Code, value);
                Core.PublishReserveChanged(target, n.Code, old, value);
            }
            state.MarkDirty();
            return TextCommandResult.Success($"Set {code} to {percent}% for {target.PlayerName}.");
        }

        private TextCommandResult SetDaysLowCommand(IServerPlayer target, string code, float days)
        {
            var state = StateOf(target.Entity);
            if (state == null) return TextCommandResult.Error("Tracking is not running for " + target.PlayerName);
            var timed = NutrientEffects.Settings.WithEffects.Where(c => NutrientEffects.Settings.Of(c).Rule == EffectRules.Timed).ToList();
            var targets = code == "all" ? timed : timed.Where(c => c == code).ToList();
            if (targets.Count == 0) return TextCommandResult.Error("Use one of: all, " + string.Join(", ", timed));
            foreach (var c in targets)
            {
                // The count only runs while the store is under a full day, so the store is lowered to just under one if needed.
                if (state.GetReserve(c) >= 100f && days > 0f) state.SetReserve(c, 99f);
                state.SetHoursWithoutFullDay(c, Math.Max(0f, days) * 24f);
            }
            Effects?.Apply(target, state, Runs(target));
            state.MarkDirty();
            return TextCommandResult.Success($"Set {code} to {days} days without a full day stored for {target.PlayerName}.");
        }

        private TextCommandResult LedgerCommand(IServerPlayer target)
        {
            var days = DietLedger.Of(target.Entity).All();
            if (days.Count == 0) return TextCommandResult.Success(target.PlayerName + " has no ledger entries yet.");
            var sb = new StringBuilder(target.PlayerName + " ate (kcal by food category):");
            foreach (var d in days)
            {
                sb.Append($"\nDay {d.Day}: {Math.Round(d.TotalKcal)}kcal");
                foreach (var kv in d.KcalByCategory.OrderByDescending(kv => kv.Value)) sb.Append($", {kv.Key} {Math.Round(kv.Value)}");
            }
            return TextCommandResult.Success(sb.ToString());
        }
    }
}
