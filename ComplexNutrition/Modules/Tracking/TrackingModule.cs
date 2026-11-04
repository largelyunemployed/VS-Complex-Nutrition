using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ComplexNutrition.Core;
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
    /// fullness (max 300, filled by each food's weight, draining at a fixed rate), eaten fats, protein and net
    /// carbohydrate go into gut pools that are absorbed over hours, and only absorption fills the calorie
    /// tank. Activity burns the tank as it burned satiety, and an empty tank starves.
    /// </summary>
    public class TrackingModule : INutritionModule
    {
        public const string ModuleId = "tracking";

        public string Id => ModuleId;
        public string[] Requires => new string[0];
        public bool EnabledByDefault => true;
        public bool IsEnabled { get; private set; }

        public ComplexNutritionModSystem Core { get; private set; }
        public TrackingSettings Settings { get; private set; } = new();
        public DigestionSettings Digest { get; private set; } = new();

        /// <summary>Fullness kept in an "empty" stomach, so vanilla's own starvation check never fires.</summary>
        public const float EmptyStomach = 0.001f;

        private ICoreServerAPI sapi;
        private long tickListener;
        private float starveTimer;

        public void Enable(ICoreAPI api, ComplexNutritionModSystem core)
        {
            Core = core;
            Settings = TrackingSettings.Load(api);
            Digest = DigestionSettings.Load(api);
            Digestion.Settings = Digest;
            IsEnabled = true;

            // The fats, protein and carbohydrate bars show the gut pools, so their cap is the pool size.
            foreach (var kv in Digest.Pools)
            {
                var def = core.Nutrients.Get(kv.Key);
                if (def != null && def.DailyTarget > 0) def.Cap = kv.Value.MaxGrams / def.DailyTarget * 100f;
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
                RegisterCommands();
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

        private double NowHours => sapi.World.Calendar.TotalHours;
        private int Today => (int)Math.Floor(sapi.World.Calendar.TotalDays);

        /// <summary>Starts tracking a player (new or returning). Time spent offline does not drain reserves.</summary>
        private void InitPlayer(IServerPlayer player)
        {
            if (player?.Entity == null) return;
            var state = NutritionState.GetOrCreate(player.Entity, Core.Nutrients, NowHours, out _);
            state.TrackingOn = true;
            state.LastUpdateHours = NowHours;
            Digestion.UseOurScale(player.Entity, Digest);
            EnsureDay(player, state);
            state.MarkDirty();
            RefreshVanillaHealthBonus(player);
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

        /// <summary>Speed of digestion from the fibre reserve: 1 at an empty reserve, 1 + the pool's bonus at the full-bonus line or above.</summary>
        public float FiberSpeed(NutritionState state, DigestionPool pool)
        {
            float full = Math.Max(1f, Digest.FiberBonusFullAt);
            float share = Math.Clamp(state.GetReserve(NutrientCodes.Fiber) / full, 0f, 1f);
            return 1f + pool.FiberSpeedBonus * share;
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
                var log = state.GetAbsorbedLog(code);
                if (lastHour == long.MinValue || hour - lastHour >= 24 || hour < lastHour)
                {
                    Array.Clear(log, 0, 24);
                }
                else
                {
                    for (long h = lastHour + 1; h <= hour; h++) log[(int)(h % 24)] = 0f;
                }

                state.GetBatches(code, out var grams, out var rates);
                float speed = FiberSpeed(state, pool);
                float absorbed = 0f, left = 0f, eta = 0f;
                var keepG = new List<float>(grams.Length);
                var keepR = new List<float>(grams.Length);
                for (int i = 0; i < grams.Length; i++)
                {
                    float rate = rates[i] * speed;
                    float take = Math.Min(grams[i], rate * hours);
                    absorbed += take;
                    float rest = grams[i] - take;
                    if (rest > 0.001f)
                    {
                        keepG.Add(rest);
                        keepR.Add(rates[i]);
                        left += rest;
                        if (rate > 0f) eta = Math.Max(eta, rest / rate);
                    }
                }
                state.SetBatches(code, keepG.ToArray(), keepR.ToArray());
                state.SetGutHours(code, eta);

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
                    state.Protein24 = Math.Min(total, Digest.ProteinMaxGrams);   // past the daily most, protein gives no benefit
                }

                var def = Core.Nutrients.Get(code);
                if (def != null)
                {
                    float old = state.GetReserve(code);
                    float now2 = def.ToPercent(left);
                    state.SetReserve(code, now2);
                    if (old != now2) Core.PublishReserveChanged(player, code, old, now2);
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
            float kcal = satiety * Core.Foods.Fallbacks.KcalPerSatiety;
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

        /// <summary>
        /// Puts eaten grams in a gut pool as a new batch, absorbed evenly over the pool's window.
        /// What does not fit in the pool passes through unused.
        /// </summary>
        private void AddToGut(IServerPlayer player, NutritionState state, NutrientDefinition n, DigestionPool pool, float grams, FoodAbsorbedInfo absorbed)
        {
            var multipliers = Core.AbsorptionMultipliers(player, n.Code);
            foreach (var m in multipliers.Values) grams *= m;
            if (multipliers.Count > 0) absorbed.Multipliers[n.Code] = multipliers;
            if (grams <= 0f) return;

            state.GetBatches(n.Code, out var batchGrams, out var rates);
            float inGut = 0f;
            foreach (var g in batchGrams) inGut += g;
            float accepted = Math.Min(grams, Math.Max(0f, pool.MaxGrams - inGut));
            if (accepted <= 0.001f) return;

            var g2 = new List<float>(batchGrams) { accepted };
            var r2 = new List<float>(rates) { accepted / Math.Max(0.01f, pool.AbsorbHours) };
            int max = Math.Max(2, Digest.MaxBatches);
            while (g2.Count > max)
            {
                // Merge the two oldest: the same grams, absorbed at their combined rate.
                g2[1] += g2[0];
                r2[1] += r2[0];
                g2.RemoveAt(0);
                r2.RemoveAt(0);
            }
            state.SetBatches(n.Code, g2.ToArray(), r2.ToArray());
            float eta = state.GetGutHours(n.Code);
            float speed = FiberSpeed(state, pool);
            state.SetGutHours(n.Code, Math.Max(eta, pool.AbsorbHours / speed));

            float old = state.GetReserve(n.Code);
            float updated = n.ToPercent(inGut + accepted);
            state.SetReserve(n.Code, updated);
            float percent = n.ToPercent(accepted);
            state.AddAbsorbedToday(n.Code, percent);
            absorbed.Absorbed[n.Code] = percent;
            Core.PublishReserveChanged(player, n.Code, old, updated);
        }

        // ---- Commands ----

        private void RegisterCommands()
        {
            var parsers = sapi.ChatCommands.Parsers;

            sapi.ChatCommands.Create("nutrition")
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
                });

            sapi.ChatCommands.GetOrCreate("cnutrition")
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
                    ? Lang.Get("complexnutrition:cmd-gut-line", Lang.Get(n.LangKey), n.Format(r, Core.Nutrients.ShowPercent), GutHoursText(state.GetGutHours(n.Code), r))
                    : Lang.Get("complexnutrition:cmd-reserve-line", Lang.Get(n.LangKey), n.Format(r, Core.Nutrients.ShowPercent), DaysText(r));
                if (withToday) line += $" (today +{Math.Round(state.GetAbsorbedToday(n.Code))}%, cap {Math.Round(n.Cap)}%)";
                sb.AppendLine(line);
                if (n.Code == NutrientCodes.Protein) sb.AppendLine(Lang.Get("complexnutrition:protein24-hover", Math.Round(state.Protein24), Math.Round(Digest.ProteinNeedGrams), Math.Round(Digest.ProteinMaxGrams)));
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>"empty", or "absorbed in about 2.5 h" for a gut pool.</summary>
        public static string GutHoursText(float hours, float reserve)
        {
            if (reserve <= 0.01f || hours <= 0f) return Lang.Get("complexnutrition:gut-empty");
            return Lang.Get("complexnutrition:gut-hours", hours < 10f ? hours.ToString("0.0") : Math.Round(hours).ToString("0"));
        }

        /// <summary>A reserve in % of a day as "0.6 days" or "12 days".</summary>
        public static string DaysText(float percent)
        {
            float days = percent / 100f;
            return days < 10f
                ? Lang.Get("complexnutrition:reserve-days", days.ToString("0.0"))
                : Lang.Get("complexnutrition:reserve-days", Math.Round(days).ToString("0"));
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
                if (pool != null)
                {
                    // A gut pool holds batches: replace them with one fresh batch of that size.
                    float grams = Math.Min(pool.MaxGrams, value / 100f * n.DailyTarget);
                    state.SetBatches(n.Code, grams > 0f ? new[] { grams } : new float[0], grams > 0f ? new[] { grams / Math.Max(0.01f, pool.AbsorbHours) } : new float[0]);
                    state.SetGutHours(n.Code, grams > 0f ? pool.AbsorbHours / FiberSpeed(state, pool) : 0f);
                    value = n.ToPercent(grams);
                }
                state.SetReserve(n.Code, value);
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
                sb.Append($"\nDay {d.Day}: {Math.Round(d.TotalKcal)} kcal");
                foreach (var kv in d.KcalByCategory.OrderByDescending(kv => kv.Value)) sb.Append($", {kv.Key} {Math.Round(kv.Value)}");
            }
            return TextCommandResult.Success(sb.ToString());
        }
    }
}
