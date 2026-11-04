using System;
using System.Linq;
using ComplexNutrition.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace ComplexNutrition.UI
{
    /// <summary>
    /// The settings menu (the Settings button in the nutrition window, or /cnutrition settings). Every toggle the mod has
    /// so far, in four categories. Display is for every player; Food Info, World and Modules only show for admins
    /// (in single player you are the admin). Each row's label has a hover that says what it does.
    /// </summary>
    public sealed class GuiDialogNutritionSettings : GuiDialog
    {
        private const double LabelWidth = 270;
        private const double ControlX = 280;
        private const double ControlWidth = 240;
        private const double RowHeight = 34;

        private readonly ComplexNutritionModSystem core;

        public GuiDialogNutritionSettings(ICoreClientAPI capi, ComplexNutritionModSystem core) : base(capi)
        {
            this.core = core;
        }

        public override string ToggleKeyCombinationCode => null;

        public override double DrawOrder => 0.21;

        private static string L(string key, params object[] args) => Lang.Get("complexnutrition:" + key, args);

        private GuiComposer composer;
        private ElementBounds bg;
        private double y;
        private int rows;

        private void Heading(string text)
        {
            y += rows == 0 ? 0 : 10;
            var b = ElementBounds.Fixed(0, y, LabelWidth + ControlWidth, 24);
            bg.WithChild(b);
            composer.AddStaticText(text, CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold).WithColor(GuiStyle.ActiveButtonTextColor), b);
            y += 28;
            rows++;
        }

        private ElementBounds Label(string label, string description)
        {
            var lb = ElementBounds.Fixed(0, y + 6, LabelWidth, 24);
            bg.WithChild(lb);
            composer.AddStaticText(label, CairoFont.WhiteSmallText(), lb);
            if (!string.IsNullOrEmpty(description))
            {
                composer.AddHoverText(description, CairoFont.WhiteSmallText(), 400, lb.FlatCopy());
            }
            var cb = ElementBounds.Fixed(ControlX, y, ControlWidth, 28);
            bg.WithChild(cb);
            y += RowHeight;
            rows++;
            return cb;
        }

        private void Switch(string label, string description, bool on, Action<bool> toggle, string key)
        {
            var cb = Label(label, description);
            composer.AddSwitch(toggle, cb.WithFixedWidth(30), key, 28);
            switchValues.Add((key, on));
        }

        private readonly System.Collections.Generic.List<(string key, bool on)> switchValues = new();

        private void Dropdown(string label, string description, string[] values, string[] names, string current, Action<string> pick, string key)
        {
            var cb = Label(label, description);
            int index = Math.Max(0, Array.IndexOf(values, current));
            composer.AddDropDown(values, names, index, (code, selected) => { if (selected) pick(code); }, cb, CairoFont.WhiteSmallText(), key);
        }

        private void Compose()
        {
            var snap = core.ServerSettings;
            bool admin = snap?.IsAdmin == true;
            switchValues.Clear();
            rows = 0;
            y = 35;

            bg = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bg.BothSizing = ElementSizing.FitToChildren;
            var dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            composer = capi.Gui.CreateCompo("complexnutrition-settings", dialogBounds)
                .AddShadedDialogBG(bg)
                .AddDialogTitleBar(L("settings-title"), () => TryClose())
                .BeginChildElements(bg);

            // ---- Display (every player) ----
            Heading(L("settings-cat-display"));
            var state = NutritionState.Of(capi.World.Player?.Entity);
            bool worldAdvanced = Digestion.WorldAdvancedDefault(capi.World);
            Dropdown(L("settings-advanced"), L("settings-advanced-desc"),
                new[] { "0", "1", "2" },
                new[] { L("settings-follow-world", L(worldAdvanced ? "settings-on" : "settings-off")), L("settings-on"), L("settings-off") },
                (state?.DisplayChoice ?? 0).ToString(),
                v => core.SendSetting(SettingKeys.Display, v), "advanced");
            string serverUnits = snap?.ShowPercent == true ? L("settings-units-percent") : L("settings-units-amount-short");
            Dropdown(L("settings-units"), L("settings-units-desc"),
                new[] { "server", "amount", "percent" },
                new[] { L("settings-units-server", serverUnits), L("settings-units-amount"), L("settings-units-percent") },
                core.Client.DisplayUnits ?? "server",
                v => core.SetClientDisplayUnits(v), "units");

            if (admin)
            {
                // ---- Food Info ----
                Heading(L("settings-cat-foodinfo"));
                Switch(L("settings-tooltips"), L("settings-tooltips-desc"), snap.ShowTooltip,
                    on => core.SendSetting(SettingKeys.ShowTooltip, on ? "true" : "false"), "tooltips");
                Dropdown(L("settings-units-default"), L("settings-units-default-desc"),
                    new[] { "amount", "percent" }, new[] { L("settings-units-amount"), L("settings-units-percent") },
                    snap.ShowPercent ? "percent" : "amount",
                    v => core.SendSetting(SettingKeys.DisplayUnits, v), "unitsdefault");
                Switch(L("settings-logfallbacks"), L("settings-logfallbacks-desc"), snap.LogFallbacks,
                    on => core.SendSetting(SettingKeys.LogFallbacks, on ? "true" : "false"), "logfallbacks");

                // ---- World ----
                Heading(L("settings-cat-world"));
                Dropdown(L("settings-start"), Lang.Get("worldattribute-" + WorldSettings.StartConditionKey + "-desc"),
                    WorldSettings.StartOptions.Select(o => o.Value).ToArray(), WorldSettings.StartOptions.Select(o => Lang.Get(o.LangKey(WorldSettings.StartConditionKey))).ToArray(),
                    snap.StartCalories ?? WorldSettings.DefaultStart,
                    v => core.SendSetting(SettingKeys.StartCalories, v), "start");
                Dropdown(L("settings-respawn"), Lang.Get("worldattribute-" + WorldSettings.RespawnConditionKey + "-desc"),
                    WorldSettings.StartOptions.Select(o => o.Value).ToArray(), WorldSettings.StartOptions.Select(o => Lang.Get(o.LangKey(WorldSettings.RespawnConditionKey))).ToArray(),
                    snap.RespawnCalories ?? WorldSettings.DefaultRespawn,
                    v => core.SendSetting(SettingKeys.RespawnCalories, v), "respawn");
                Switch(L("settings-advanced-default"), L("settings-advanced-default-desc"), snap.AdvancedDefault,
                    on => core.SendSetting(SettingKeys.AdvancedDefault, on ? "true" : "false"), "advanceddefault");

                Switch(L("settings-meatinpots"), Lang.Get("worldattribute-" + WorldSettings.MeatInPotsKey + "-desc"), snap.MeatInPots,
                    on => core.SendSetting(SettingKeys.MeatInPots, on ? "true" : "false"), "meatinpots");

                // ---- Modules ----
                Heading(L("settings-cat-modules"));
                Switch(L("settings-module-fooddata"), L("settings-module-fooddata-desc"), ModuleOn(snap, ModuleIds.FoodData),
                    on => core.SendSetting(SettingKeys.ModulePrefix + ModuleIds.FoodData, on ? "true" : "false"), "module-fooddata");
                Switch(L("settings-module-tracking"), L("settings-module-tracking-desc"), ModuleOn(snap, ModuleIds.Tracking),
                    OnTrackingToggled, "module-tracking");
            }
            else
            {
                y += 8;
                var nb = ElementBounds.Fixed(0, y, LabelWidth + ControlWidth, 40);
                bg.WithChild(nb);
                composer.AddStaticText(L("settings-admin-note"), CairoFont.WhiteDetailText().WithColor(new[] { 0.65, 0.65, 0.65, 1.0 }), nb);
                y += 40;
            }

            var close = ElementBounds.Fixed(0, y + 12, 0, 0).WithAlignment(EnumDialogArea.RightFixed).WithFixedPadding(20, 4);
            bg.WithChild(close);
            composer.AddSmallButton(L("window-close"), () => { TryClose(); return true; }, close);

            SingleComposer = composer.EndChildElements().Compose();
            foreach (var (key, on) in switchValues) SingleComposer.GetSwitch(key)?.SetValue(on);
        }

        private static bool ModuleOn(SettingsSnapshot snap, string id) => snap.Modules != null && snap.Modules.TryGetValue(id, out var on) && on;

        /// <summary>Switching tracking converts every player's state, so it asks first and says what will happen.</summary>
        private void OnTrackingToggled(bool on)
        {
            // Put the switch back until the server confirms the change.
            SingleComposer.GetSwitch("module-tracking")?.SetValue(!on);
            // Our Stats panel only comes back once the character window is reopened, so the box says so.
            new GuiDialogConfirmTip(capi, L(on ? "settings-confirm-on" : "settings-confirm-off"), on ? L("settings-confirm-on-tip") : null, ok =>
            {
                if (ok) core.SendSetting(SettingKeys.ModulePrefix + ModuleIds.Tracking, on ? "true" : "false");
            }).TryOpen();
        }

        /// <summary>New settings from the server: redraw if open.</summary>
        public void Refresh()
        {
            if (IsOpened()) Compose();
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            Compose();
        }
    }
}
