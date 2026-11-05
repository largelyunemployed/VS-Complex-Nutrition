using System.Collections.Generic;
using Newtonsoft.Json;
using ProtoBuf;

namespace ComplexNutrition.Core
{
    /// <summary>
    /// The server's settings as one player's client sees them: everything an admin can change, whether this player
    /// is an admin, and the world settings. Sent on join and after every change.
    /// </summary>
    public class SettingsSnapshot
    {
        public bool IsAdmin;
        public bool ShowTooltip = true;
        public bool LogFallbacks = true;
        public string DisplayUnits = "amount";
        public Dictionary<string, bool> Modules = new();
        /// <summary>The world's start condition (% of a day in the calorie reserve), or null when the world has none.</summary>
        public string StartCalories;
        public string RespawnCalories;
        public bool AdvancedDefault;
        public bool MeatInPots;
        public bool DiminishingReturns = true;
        public string WildHarvest = WorldSettings.DefaultWildHarvest;

        [JsonIgnore] public bool ShowPercent => string.Equals(DisplayUnits, "percent", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Server to client: a <see cref="SettingsSnapshot"/>, as JSON.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class SettingsSnapshotPacket
    {
        public string Json;
    }

    /// <summary>Client to server: change one setting. The server checks the player may change it.</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class SettingsChangePacket
    {
        public string Key;
        public string Value;
    }

    /// <summary>Server to client: open the settings menu (from /cnutrition settings).</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class OpenSettingsPacket
    {
        public bool Open = true;
    }

    /// <summary>Client to server: send me my history (the nutrition window is open).</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class HistoryRequestPacket
    {
        public bool Want = true;
    }

    /// <summary>Server to client: a <see cref="HistoryData"/>, as JSON (empty when tracking is off for the player).</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class HistoryPacket
    {
        public string Json;
    }

    /// <summary>Setting keys sent in a <see cref="SettingsChangePacket"/>.</summary>
    public static class SettingKeys
    {
        /// <summary>Any player: their own advanced display choice, 0 (follow the world), 1 (on) or 2 (off).</summary>
        public const string Display = "display";
        public const string ShowTooltip = "showTooltip";
        public const string LogFallbacks = "logFallbacks";
        public const string DisplayUnits = "displayUnits";
        public const string StartCalories = "startCalories";
        public const string RespawnCalories = "respawnCalories";
        public const string AdvancedDefault = "advancedDefault";
        public const string MeatInPots = "meatInPots";
        public const string DiminishingReturns = "diminishingReturns";
        public const string WildHarvest = "wildHarvest";
        /// <summary>Prefix for a module switch: "module:tracking".</summary>
        public const string ModulePrefix = "module:";
    }

    /// <summary>
    /// Settings that never leave the player's computer, in ModConfig/complexnutrition-client.json.
    /// </summary>
    public class ClientSettings
    {
        public const string FileName = "complexnutrition-client.json";

        /// <summary>"server" (use the server's default), "amount" or "percent".</summary>
        public string DisplayUnits = "server";

        /// <summary>Food tooltips show only calories and fullness until Ctrl is held (v0.3.3).</summary>
        public bool ShortTooltips = true;

        /// <summary>The little icon right of the hotbar that warns when something runs low (v0.3.3).</summary>
        public bool HudIcon = true;
    }
}
