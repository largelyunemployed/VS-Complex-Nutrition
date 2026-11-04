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
    /// tank. Activity burns the tank as it burned satiety, and an empty tank starves.
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
                tickListener = sapi.Event.RegisterGameTickListener(OnTick, (int)(Math.Max(0.5f, Settings.UpdateSeconds) * 1000));
                foreach (IServerPlayer player in sapi.World.AllOnlinePlayers) InitPlayer(player);
            }
            else if (api is ICoreClientAPI capi)
            {
                CharacterStatsPatch.Apply(capi, core);
                FullnessDisplayPatch.Apply(core);
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
                sapi.Event.UnregisterGameTickListener(tickListener);
                HungerPatches.Remove();
                foreach (IServerPlayer player in sapi.World.AllOnlinePlayers)
                {
                    var state = NutritionState.Of(player.Entity);
                    if (state == null) continue;
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
            state.MarkDirty();
            RefreshVanillaHealthBonus(player);
        }

        /// <summary>
        /// A character new to Complex Nutrition starts with the calorie tank the world's start condition picks:
        /// Easy 7 days (full), Normal 5, Hard 3, Brutal 1, Rock Bottom 1% of a day. Only on first join, never on respawn.
        /// </summary>
        private void ApplyStartCondition(IServerPlayer player, NutritionState state)
        {
            var def = Core.Nutrients.Get(NutrientCodes.Calories);
            if (def == null) return;
            float? start = WorldSettings.StartCalories(sapi.World);
            if (start == null) return;
            state.SetReserve(def.Code, Math.Max(0f, Math.Min(def.Cap, start.Value)));
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

        /// <summary>Vanilla gives half a stomach on revive; the calorie tank gets at least a day's worth, so a starved player can recover.</summary>
        private void OnRespawn(IServerPlayer player)
        {
            var state = player?.Entity == null ? null : StateOf(player.Entity);
            var def = Core.Nutrients.Get(NutrientCodes.Calories);
            if (state == null || def == null) return;
            float old = state.GetReserve(def.Code);
            if (old >= Digest.ReviveCalories) return;
            state.SetReserve(def.Code, Digest.ReviveCalories);
            state.MarkDirty();
            Core.PublishReserveChanged(player, def.Code, old, Digest.ReviveCalories);
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
            if (day != int.MinValue && day < today)
            {
                Core.PublishDailyRollover(player, ledger.Get(day));
            }
            state.ClearToday();
            state.Day = today;
            ledger.Prune(today, Settings.LedgerDays);
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
                if (scanHeld) RecordHeld(player);

                var mode = player.WorldData.CurrentGameMode;
                bool drains = player.Entity.Alive && mode != EnumGameMode.Creative && mode != EnumGameMode.Spectator;
                if (starveNow && drains && state.GetReserve(NutrientCodes.Calories) <= 0f)
                {
                    // Vanilla's starvation, moved from an empty stomach to an empty calorie tank.
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
                    if (hunger != null)
                    {
                        float full = hunger.Saturation;
                        float next = Math.Max(EmptyStomach, full - Digest.FullnessDrainPerHour * (float)hours);
                        if (next != full) hunger.Saturation = next;
                    }

                    DigestStep(player, state, (float)hours, now);
                }
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
                var log = state.GetAbsorbedLog(code);
                if (lastHour == long.MinValue || hour - lastHour >= 24 || hour < lastHour)
                {
                    Array.Clear(log, 0, 24);
                }
                else
                {
                    for (long h = lastHour + 1; h <= hour; h++) log[(int)(h % 24)] = 0f;
                }

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
                    kcal += pool.Effective(window, absorbed) * pool.KcalPerGram;
                    log[(int)(hour % 24)] += absorbed;
                }
                state.SetAbsorbedLog(code, log);
                if (code == NutrientCodes.Protein)
                {
                    float total = 0f;
                    foreach (var g in log) total += g;
                    state.Protein24 = Math.Min(total, Digest.ProteinMaxGrams);   // the bar's top
                }
            }
            state.AbsorbedHour = hour;

            var cal = Core.Nutrients.Get(NutrientCodes.Calories);
            if (cal != null && kcal > 0f)
            {
                float old = state.GetReserve(cal.Code);
                float updated = Math.Max(old, Math.Min(cal.Cap, old + cal.ToPercent(kcal)));   // a full tank wastes the rest
                state.SetReserve(cal.Code, updated);
                state.AddAbsorbedToday(cal.Code, updated - old);
                Core.PublishReserveChanged(player, cal.Code, old, updated);
            }
        }

        /// <summary>Burns calories as vanilla burns satiety, so activity, cold and the hunger-rate stat all count.</summary>
        internal void BurnSatiety(IServerPlayer player, float satiety)
        {
            var state = StateOf(player.Entity);
            var def = Core.Nutrients.Get(NutrientCodes.Calories);
            if (state == null || def == null || satiety <= 0f) return;
            float kcal = satiety * Digest.KcalPerSatiety;
            float old = state.GetReserve(def.Code);
            float now = Math.Max(0f, old - def.ToPercent(kcal));
            state.SetReserve(def.Code, now);
            state.MarkDirty();
            Core.PublishReserveChanged(player, def.Code, old, now);
        }

        /// <summary>Absorption above the sitting limit: the extra x adds scale * ln(1 + x / scale).</summary>
        public float SittingCurve(float total, float limit)
        {
            if (total <= limit) return total;
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
                // Calories come from digestion only, never straight from eating.
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
                float updated = Math.Max(old, Math.Min(n.Cap, old + gained));   // never cut a reserve already over the cap
                state.SetReserve(n.Code, updated);
                state.AddAbsorbedToday(n.Code, gained);
                absorbed.Absorbed[n.Code] = gained;
                Core.PublishReserveChanged(player, n.Code, old, updated);
            }

            DietLedger.Of(player.Entity).Add(Today, eaten.KcalByCategory);
            state.MarkDirty();
            Core.PublishFoodAbsorbed(absorbed);
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
                if (withToday) line += $" (today +{Math.Round(state.GetAbsorbedToday(n.Code))}%, cap {Math.Round(n.Cap)}%)";
                sb.AppendLine(line);
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
