using System;
using System.Collections.Generic;
using System.Linq;
using ComplexNutrition.UI;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace ComplexNutrition.Core
{
    /// <summary>
    /// v0.3.0: the settings menu's plumbing and the nutrition window. Settings live where they belong: the client
    /// file for what only changes your own screen, the player's data for their display choice, the server's
    /// config file for what the admin decides, and the world config for world settings. The client asks the
    /// server for each change over our network channel; the server checks the player may make it, applies it,
    /// saves it and sends every client the new settings.
    /// </summary>
    public partial class ComplexNutritionModSystem
    {
        public const string ChannelName = ModId;
        public const string HotkeyCode = "complexnutrition-window";

        /// <summary>Client: the server's settings as last sent, or null before the first.</summary>
        public SettingsSnapshot ServerSettings { get; private set; }

        /// <summary>Client: this computer's own settings.</summary>
        public ClientSettings Client { get; private set; } = new();

        private ICoreServerAPI sapi;
        private ICoreClientAPI capi;
        private IServerNetworkChannel serverChannel;
        private IClientNetworkChannel clientChannel;
        private GuiDialogNutrition window;
        private GuiDialogNutritionSettings settingsDialog;
        private TrackingSettings clientTracking;
        private DigestionSettings clientDigest;

        /// <summary>Whether food tooltips show the nutrient block. The admin decides; clients follow the server.</summary>
        public bool ShowTooltip => api?.Side == EnumAppSide.Client && ServerSettings != null ? ServerSettings.ShowTooltip : Config.ShowTooltip;

        /// <summary>
        /// Show quantities in % of a day's need rather than grams and kcal? On the client the player's own choice, or the
        /// server's default; on the server the config's "displayUnits" (for command output).
        /// </summary>
        public bool ShowPercent => api?.Side == EnumAppSide.Client ? ClientShowPercent : Config.ShowPercent;

        /// <summary>Client: amounts in % of a day's need? The player's own choice, or the server's default.</summary>
        private bool ClientShowPercent
        {
            get
            {
                string own = Client?.DisplayUnits ?? "server";
                if (own == "percent") return true;
                if (own == "amount") return false;
                return ServerSettings?.ShowPercent ?? Config.ShowPercent;
            }
        }

        public override void Start(ICoreAPI api)
        {
            api.Network.RegisterChannel(ChannelName)
                .RegisterMessageType<SettingsSnapshotPacket>()
                .RegisterMessageType<SettingsChangePacket>()
                .RegisterMessageType<OpenSettingsPacket>()
                .RegisterMessageType<HistoryRequestPacket>()
                .RegisterMessageType<HistoryPacket>();
        }

        // ---- Server ----

        private void StartServerSettings(ICoreServerAPI api)
        {
            sapi = api;
            serverChannel = api.Network.GetChannel(ChannelName);
            serverChannel.SetMessageHandler<SettingsChangePacket>(OnSettingsChange);
            serverChannel.SetMessageHandler<HistoryRequestPacket>(OnHistoryRequest);
            api.Event.PlayerNowPlaying += SendSettings;

            api.ChatCommands.GetOrCreate("cnutrition")
                .BeginSubCommand("settings")
                    .WithDescription("Open the Complex Nutrition settings menu")
                    .RequiresPlayer()
                    .RequiresPrivilege(Privilege.chat)
                    .HandleWith(args =>
                    {
                        if (args.Caller.Player is IServerPlayer p) serverChannel.SendPacket(new OpenSettingsPacket(), p);
                        return TextCommandResult.Success();
                    })
                .EndSubCommand();

            // Poultry and bushmeat in pots for any world, including ones made before v0.3.0 (same as the settings menu).
            api.ChatCommands.GetOrCreate("cnutrition")
                .BeginSubCommand("meatinpots")
                    .WithDescription("Let poultry and bushmeat go into stews and soups: /cnutrition meatinpots <on|off>. Takes effect after a restart")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(api.ChatCommands.Parsers.OptionalWordRange("state", "on", "off"))
                    .HandleWith(args =>
                    {
                        if (!args.Parsers[0].IsMissing)
                        {
                            sapi.World.Config.SetString(WorldSettings.MeatInPotsKey, (string)args[0] == "on" ? "true" : "false");
                            SendSettingsToAll();
                        }
                        bool now = WorldSettings.Bool(sapi.World, WorldSettings.MeatInPotsKey);
                        return TextCommandResult.Success(Lang.Get(now ? "complexnutrition:cmd-meatinpots-on" : "complexnutrition:cmd-meatinpots-off"));
                    })
                .EndSubCommand();

            // The same switch from the server console, for dedicated servers.
            api.ChatCommands.GetOrCreate("cnutrition")
                .BeginSubCommand("module")
                    .WithDescription("Switch a module on or off while the server runs: /cnutrition module <fooddata|tracking> <on|off>")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(api.ChatCommands.Parsers.Word("module"), api.ChatCommands.Parsers.WordRange("state", "on", "off"))
                    .HandleWith(args =>
                    {
                        var module = modules.FirstOrDefault(m => m.Id == (string)args[0]);
                        if (module == null) return TextCommandResult.Error("Unknown module. Use one of: " + string.Join(", ", modules.Select(m => m.Id)));
                        bool on = (string)args[1] == "on";
                        Config.Modules[module.Id] = on;
                        SetModuleRunning(module, on);
                        sapi.StoreModConfig(Config, ComplexNutritionConfig.FileName);
                        SendSettingsToAll();
                        return TextCommandResult.Success($"Module {module.Id} is {(module.IsEnabled ? "on" : "off")}.");
                    })
                .EndSubCommand();
        }

        /// <summary>
        /// Server: builds a player's history for the nutrition window. Set by the tracking module while it runs; null
        /// otherwise, and the window then gets an empty answer.
        /// </summary>
        public System.Func<IServerPlayer, HistoryData> HistoryProvider { get; set; }

        private readonly Dictionary<string, long> lastHistorySent = new();

        private void OnHistoryRequest(IServerPlayer player, HistoryRequestPacket packet)
        {
            if (player == null) return;
            // At most one answer a second per player, however often a client asks.
            long now = sapi.World.ElapsedMilliseconds;
            if (lastHistorySent.TryGetValue(player.PlayerUID, out long last) && now - last < 1000) return;
            lastHistorySent[player.PlayerUID] = now;
            var data = HistoryProvider?.Invoke(player);
            serverChannel.SendPacket(new HistoryPacket { Json = data == null ? "" : JsonConvert.SerializeObject(data) }, player);
        }

        private static bool IsAdmin(IServerPlayer player) => player != null && player.HasPrivilege(Privilege.controlserver);

        private SettingsSnapshot SnapshotFor(IServerPlayer player) => new()
        {
            IsAdmin = IsAdmin(player),
            ShowTooltip = Config.ShowTooltip,
            LogFallbacks = Config.LogFallbacks,
            DisplayUnits = Config.ShowPercent ? "percent" : "amount",
            Modules = modules.ToDictionary(m => m.Id, m => m.IsEnabled),
            StartCalories = WorldSettings.StartCalories(sapi.World)?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            RespawnCalories = WorldSettings.RespawnCalories(sapi.World).ToString(System.Globalization.CultureInfo.InvariantCulture),
            AdvancedDefault = Digestion.WorldAdvancedDefault(sapi.World),
            MeatInPots = WorldSettings.Bool(sapi.World, WorldSettings.MeatInPotsKey),
        };

        private void SendSettings(IServerPlayer player)
        {
            if (player == null || serverChannel == null) return;
            serverChannel.SendPacket(new SettingsSnapshotPacket { Json = JsonConvert.SerializeObject(SnapshotFor(player)) }, player);
        }

        private void SendSettingsToAll()
        {
            foreach (IServerPlayer p in sapi.World.AllOnlinePlayers)
            {
                if (p.ConnectionState == EnumClientState.Playing) SendSettings(p);
            }
        }

        private void OnSettingsChange(IServerPlayer from, SettingsChangePacket packet)
        {
            string key = packet?.Key ?? "";
            string value = packet?.Value ?? "";
            bool on = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

            if (key == SettingKeys.Display)
            {
                var state = NutritionState.Of(from.Entity);
                if (state != null && int.TryParse(value, out int choice) && choice >= 0 && choice <= 2)
                {
                    state.DisplayChoice = choice;
                    state.MarkDirty();
                }
                SendSettings(from);
                return;
            }

            if (!IsAdmin(from))
            {
                api.Logger.Notification("[complexnutrition] {0} tried to change {1} without admin rights.", from.PlayerName, key);
                SendSettings(from);
                return;
            }

            bool saveConfig = true;
            switch (key)
            {
                case SettingKeys.ShowTooltip: Config.ShowTooltip = on; break;
                case SettingKeys.LogFallbacks: Config.LogFallbacks = on; break;
                case SettingKeys.DisplayUnits:
                    if (value != "amount" && value != "percent") return;
                    Config.DisplayUnits = value;
                    break;
                case SettingKeys.StartCalories:
                    if (!WorldSettings.IsStartOption(value)) return;
                    sapi.World.Config.SetString(WorldSettings.StartConditionKey, value);
                    saveConfig = false;
                    break;
                case SettingKeys.RespawnCalories:
                    if (!WorldSettings.IsStartOption(value)) return;
                    sapi.World.Config.SetString(WorldSettings.RespawnConditionKey, value);
                    saveConfig = false;
                    break;
                case SettingKeys.MeatInPots:
                    // Recipes are patched when the world loads, so this takes effect after a restart.
                    sapi.World.Config.SetString(WorldSettings.MeatInPotsKey, on ? "true" : "false");
                    saveConfig = false;
                    break;
                case SettingKeys.AdvancedDefault:
                    sapi.World.Config.SetBool(WorldSettings.AdvancedDisplayKey, on);
                    saveConfig = false;
                    break;
                default:
                    if (!key.StartsWith(SettingKeys.ModulePrefix)) return;
                    var module = modules.FirstOrDefault(m => m.Id == key.Substring(SettingKeys.ModulePrefix.Length));
                    if (module == null) return;
                    Config.Modules[module.Id] = on;
                    SetModuleRunning(module, on);
                    break;
            }
            if (saveConfig) sapi.StoreModConfig(Config, ComplexNutritionConfig.FileName);
            api.Logger.Notification("[complexnutrition] {0} set {1} to {2}.", from.PlayerName, key, value);
            SendSettingsToAll();
        }

        /// <summary>
        /// Starts or stops a module while the game runs, on this side. A module switched off is told so after it stops
        /// (stopping tracking puts every online player back on vanilla values; see <see cref="INutritionModule.OnSwitchedOff"/>).
        /// </summary>
        internal void SetModuleRunning(INutritionModule module, bool on)
        {
            if (module == null || module.IsEnabled == on) return;
            if (on)
            {
                var missing = module.Requires.Where(r => !modules.Any(m => m.Id == r && m.IsEnabled)).ToArray();
                if (missing.Length > 0)
                {
                    api.Logger.Notification("[complexnutrition] Module {0} stays off: needs {1}.", module.Id, string.Join(", ", missing));
                    return;
                }
                module.Enable(api, this);
                api.Logger.Notification("[complexnutrition] Module {0} switched on.", module.Id);
                return;
            }

            foreach (var dependent in modules.Where(m => m.IsEnabled && m.Requires.Contains(module.Id)).ToList()) SetModuleRunning(dependent, false);
            module.Disable(api);
            api.Logger.Notification("[complexnutrition] Module {0} switched off.", module.Id);
            module.OnSwitchedOff(api);
        }

        // ---- Client ----

        private void StartClientSettings(ICoreClientAPI api)
        {
            capi = api;
            try
            {
                Client = api.LoadModConfig<ClientSettings>(ClientSettings.FileName);
            }
            catch (Exception e)
            {
                api.Logger.Error("[complexnutrition] Could not read {0}, using defaults: {1}", ClientSettings.FileName, e.Message);
            }
            if (Client == null)
            {
                // First run: keep what this computer's own config file chose before v0.3, if it chose percent.
                Client = new ClientSettings { DisplayUnits = Config.ShowPercent ? "percent" : "server" };
            }
            api.StoreModConfig(Client, ClientSettings.FileName);

            clientChannel = api.Network.GetChannel(ChannelName);
            clientChannel.SetMessageHandler<SettingsSnapshotPacket>(OnSettingsSnapshot);
            clientChannel.SetMessageHandler<OpenSettingsPacket>(_ => OpenSettings());
            clientChannel.SetMessageHandler<HistoryPacket>(OnHistory);

            api.Input.RegisterHotKey(HotkeyCode, Lang.Get("complexnutrition:hotkey-window"), GlKeys.N, HotkeyType.GUIOrOtherControls, altPressed: true);
            api.Input.SetHotKeyHandler(HotkeyCode, _ => { ToggleWindow(); return true; });
        }

        private void OnSettingsSnapshot(SettingsSnapshotPacket packet)
        {
            SettingsSnapshot snap;
            try
            {
                snap = JsonConvert.DeserializeObject<SettingsSnapshot>(packet?.Json ?? "");
            }
            catch (Exception e)
            {
                capi.Logger.Error("[complexnutrition] Could not read the server's settings: {0}", e.Message);
                return;
            }
            if (snap == null) return;
            ServerSettings = snap;

            // The client follows the server's module switches, so a module switched on or off in the menu takes effect here too.
            foreach (var module in OrderByDependencies(modules).ToList())
            {
                if (snap.Modules != null && snap.Modules.TryGetValue(module.Id, out bool on)) SetModuleRunning(module, on);
            }

            // Keep the client's copy of the world settings in step, so its hovers follow a change made in the menu.
            var cfg = capi.World?.Config;
            if (cfg != null)
            {
                cfg.SetBool(WorldSettings.AdvancedDisplayKey, snap.AdvancedDefault);
                if (snap.StartCalories != null) cfg.SetString(WorldSettings.StartConditionKey, snap.StartCalories);
                if (snap.RespawnCalories != null) cfg.SetString(WorldSettings.RespawnConditionKey, snap.RespawnCalories);
            }

            settingsDialog?.Refresh();
            if (window?.IsOpened() == true) window.Refresh(true);
        }

        /// <summary>Client: the last history the server sent, or null (none yet, or tracking is off).</summary>
        public HistoryData History { get; private set; }

        /// <summary>Client: asks the server for the player's history; the answer refreshes the window.</summary>
        public void RequestHistory()
        {
            clientChannel?.SendPacket(new HistoryRequestPacket());
        }

        private void OnHistory(HistoryPacket packet)
        {
            try
            {
                History = string.IsNullOrEmpty(packet?.Json) ? null : JsonConvert.DeserializeObject<HistoryData>(packet.Json);
            }
            catch (Exception e)
            {
                capi.Logger.Error("[complexnutrition] Could not read the history the server sent: {0}", e.Message);
                History = null;
            }
            if (window?.IsOpened() == true) window.Refresh();
        }

        /// <summary>Client: asks the server to change a setting.</summary>
        public void SendSetting(string key, string value)
        {
            clientChannel?.SendPacket(new SettingsChangePacket { Key = key, Value = value });
        }

        /// <summary>Client: the player's own amounts-or-percent choice ("server", "amount" or "percent"), saved on this computer.</summary>
        public void SetClientDisplayUnits(string value)
        {
            if (value != "server" && value != "amount" && value != "percent") return;
            Client.DisplayUnits = value;
            capi.StoreModConfig(Client, ClientSettings.FileName);
            if (window?.IsOpened() == true) window.Refresh(true);
        }

        public void ToggleWindow()
        {
            if (capi == null) return;
            window ??= new GuiDialogNutrition(capi, this);
            if (window.IsOpened()) window.TryClose();
            else window.TryOpen();
        }

        public void OpenSettings()
        {
            if (capi == null) return;
            settingsDialog ??= new GuiDialogNutritionSettings(capi, this);
            if (!settingsDialog.IsOpened()) settingsDialog.TryOpen();
            else settingsDialog.Refresh();
        }

        // ---- Nutrition window data ----

        private string[] knownCodes;
        private List<KnownFood> known = new();

        /// <summary>Client: what the nutrition window shows, read from the player's synced data.</summary>
        public NutritionPageData BuildPageData()
        {
            var entity = capi?.World?.Player?.Entity;
            var state = NutritionState.Of(entity);
            if (state != null && !state.TrackingOn) state = null;
            clientTracking ??= TrackingSettings.Load(capi);
            clientDigest ??= DigestionSettings.Load(capi);
            var hunger = entity?.WatchedAttributes.GetTreeAttribute("hunger");
            var health = entity?.WatchedAttributes.GetTreeAttribute("health");

            var codes = (entity?.WatchedAttributes[NutritionState.HeldKey] as StringArrayAttribute)?.value;
            if (codes != knownCodes)
            {
                knownCodes = codes;
                known = new List<KnownFood>();
                foreach (var code in codes ?? new string[0])
                {
                    var loc = new AssetLocation(code);
                    CollectibleObject coll = (CollectibleObject)capi.World.GetItem(loc) ?? capi.World.GetBlock(loc);
                    if (coll == null || coll.Id == 0) continue;
                    var food = Foods.Resolve(coll);
                    if (food == null) continue;
                    known.Add(new KnownFood { Name = coll.GetHeldItemName(new ItemStack(coll)), Food = food });
                }
            }

            return new NutritionPageData
            {
                Nutrients = Nutrients,
                Digest = Digestion.Settings ?? clientDigest,
                Tracking = clientTracking,
                State = state,
                Fullness = hunger?.GetFloat("currentsaturation") ?? 0f,
                MaxFullness = hunger?.GetFloat("maxsaturation") ?? 0f,
                Health = health?.GetFloat("currenthealth") ?? 0f,
                MaxHealth = health?.GetFloat("maxhealth") ?? 0f,
                ShowPercent = ShowPercent,
                Known = known,
                History = state == null ? null : History,
            };
        }

        private void DisposeSettings()
        {
            window?.TryClose();
            window?.Dispose();
            settingsDialog?.TryClose();
            settingsDialog?.Dispose();
            window = null;
            settingsDialog = null;
        }
    }
}
