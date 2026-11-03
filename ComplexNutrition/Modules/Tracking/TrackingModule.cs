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
    /// switching it off brings both back. Satiety is left to vanilla.
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

        private ICoreServerAPI sapi;
        private long tickListener;

        public void Enable(ICoreAPI api, ComplexNutritionModSystem core)
        {
            Core = core;
            Settings = TrackingSettings.Load(api);
            IsEnabled = true;

            if (api is ICoreServerAPI serverApi)
            {
                sapi = serverApi;
                core.RequireEatingHooks();
                core.FoodEaten += OnFoodEaten;
                HungerPatches.Apply(this);
                sapi.Event.PlayerJoin += InitPlayer;
                sapi.Event.PlayerNowPlaying += InitPlayer;
                tickListener = sapi.Event.RegisterGameTickListener(OnTick, (int)(Math.Max(0.5f, Settings.UpdateSeconds) * 1000));
                foreach (IServerPlayer player in sapi.World.AllOnlinePlayers) InitPlayer(player);
                RegisterCommands();
            }
            else if (api is ICoreClientAPI capi)
            {
                CharacterStatsPatch.Apply(capi, core);
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
                sapi.Event.UnregisterGameTickListener(tickListener);
                HungerPatches.Remove();
                foreach (IServerPlayer player in sapi.World.AllOnlinePlayers)
                {
                    var state = NutritionState.Of(player.Entity);
                    if (state == null) continue;
                    state.TrackingOn = false;
                    state.MarkDirty();
                    RefreshVanillaHealthBonus(player);
                }
                sapi = null;
            }
            else
            {
                CharacterStatsPatch.Remove();
            }
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
            EnsureDay(player, state);
            state.MarkDirty();
            RefreshVanillaHealthBonus(player);
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

                double now = NowHours;
                double hours = now - state.LastUpdateHours;
                if (hours < 0)
                {
                    state.LastUpdateHours = now;   // time was set back
                    continue;
                }
                if (hours < Settings.MinDrainHours) continue;
                state.LastUpdateHours = now;

                var mode = player.WorldData.CurrentGameMode;
                bool drains = player.Entity.Alive && mode != EnumGameMode.Creative && mode != EnumGameMode.Spectator;
                if (drains)
                {
                    foreach (var n in Core.Nutrients.All)
                    {
                        if (n.IsCalories) continue;   // calories follow the hunger rate, see HungerPatches
                        float old = state.GetReserve(n.Code);
                        float now2 = Math.Max(0f, old - n.DrainPerDay * (float)(hours / 24.0));
                        state.SetReserve(n.Code, now2);
                        Core.PublishReserveChanged(player, n.Code, old, now2);
                    }
                }
                state.MarkDirty();
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
                        : TextCommandResult.Success(ReserveReport(state));
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
                            : TextCommandResult.Success(target.PlayerName + ":\n" + ReserveReport(state, true));
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

        private string ReserveReport(NutritionState state, bool withToday = false)
        {
            var sb = new StringBuilder();
            foreach (var n in Core.Nutrients.All)
            {
                float r = state.GetReserve(n.Code);
                string line = Lang.Get("complexnutrition:cmd-reserve-line", Lang.Get(n.LangKey), n.Format(r, Core.Nutrients.ShowPercent), DaysText(r));
                if (withToday) line += $" (today +{Math.Round(state.GetAbsorbedToday(n.Code))}%, cap {n.Cap}%)";
                sb.AppendLine(line);
            }
            return sb.ToString().TrimEnd();
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
