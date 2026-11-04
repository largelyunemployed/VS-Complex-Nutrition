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
        /// <summary>The world's start condition (% of a day in the calorie tank), or null when the world has none.</summary>
        public string StartCalories;
        public bool AdvancedDefault;
        public bool MeatInPots;

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

    /// <summary>Server to client: open the settings menu (from /nutrition settings).</summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class OpenSettingsPacket
    {
        public bool Open = true;
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
        public const string AdvancedDefault = "advancedDefault";
        public const string MeatInPots = "meatInPots";
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
    }
}
