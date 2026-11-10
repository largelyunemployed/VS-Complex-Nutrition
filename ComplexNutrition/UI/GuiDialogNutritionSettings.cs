using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ComplexNutrition.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace ComplexNutrition.UI
{
    /// <summary>
    /// The settings menu (the Settings button in the nutrition window, or /cnutrition settings). Since 0.4.2b it has two tabs:
    /// Your Settings, which every player has and which only change what that player sees, and World Settings, which only
    /// admins see (anyone with the game's controlserver privilege; in single player you are the admin). Nothing changes until
    /// Save is pressed: every change waits in a list, Save sends them all, and Cancel or the close button drops them.
    /// Each row's label has a hover that says what it does.
    /// </summary>
    public sealed class GuiDialogNutritionSettings : GuiDialog
    {
        private const double LabelWidth = 270;
        private const double ControlX = 280;
        private const double ControlWidth = 240;
        private const double RowHeight = 34;
        private const double FullWidth = ControlX + ControlWidth;

        private const int YourSettingsTab = 0;
        private const int WorldSettingsTab = 1;

        // Names for the player's own settings while they wait to be saved. Server settings wait under their SettingKeys name.
        private const string UnitsChange = "client:units";
        private const string ShortTooltipsChange = "client:shorttooltips";
        private const string HudIconChange = "client:hudicon";

        private readonly ComplexNutritionModSystem core;

        /// <summary>Changes made since the menu opened or was last saved, by setting name, each with its new value.</summary>
        private readonly Dictionary<string, string> unsavedChanges = new();

        private int tab = YourSettingsTab;

        public GuiDialogNutritionSettings(ICoreClientAPI capi, ComplexNutritionModSystem core) : base(capi)
        {
            this.core = core;
        }

        public override string ToggleKeyCombinationCode => null;

        public override double DrawOrder => 0.21;

        private static string L(string key, params object[] args) => Lang.Get("complexnutrition:" + key, args);

        private static string OnOff(bool on) => on ? "true" : "false";

        // ---- What each control shows: the unsaved change if there is one, otherwise the saved value ----

        private string ShownValue(string name, string savedValue) =>
            unsavedChanges.TryGetValue(name, out var value) ? value : savedValue;

        private bool ShownOn(string name, bool savedOn) => ShownValue(name, OnOff(savedOn)) == "true";

        /// <summary>Remembers a change until Save. Putting a setting back to its saved value forgets the change.</summary>
        private void Change(string name, string value, string savedValue)
        {
            if (value == savedValue) unsavedChanges.Remove(name);
            else unsavedChanges[name] = value;
        }

        // ---- Building the rows ----

        private GuiComposer composer;
        private ElementBounds bg;
        private double y;
        private int rows;
        private readonly List<(string key, bool on)> switchValues = new();
        private readonly List<(string key, int value, int min, int max, int step, Func<int, string> text)> sliderValues = new();

        private void Heading(string text)
        {
            y += rows == 0 ? 0 : 10;
            var b = ElementBounds.Fixed(0, y, FullWidth, 24);
            bg.WithChild(b);
            composer.AddStaticText(text, CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold).WithColor(GuiStyle.ActiveButtonTextColor), b);
            y += 28;
            rows++;
        }

        /// <summary>A line of small grey text across the menu.</summary>
        private void Note(string text, double height)
        {
            var b = ElementBounds.Fixed(0, y, FullWidth, height);
            bg.WithChild(b);
            composer.AddStaticText(text, CairoFont.WhiteDetailText().WithColor(new[] { 0.65, 0.65, 0.65, 1.0 }), b);
            y += height;
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

        /// <summary>An on/off switch for the setting called <paramref name="name"/>, saved as "true" or "false".</summary>
        private void Switch(string label, string description, string name, bool savedOn)
        {
            var cb = Label(label, description);
            composer.AddSwitch(on => Change(name, OnOff(on), OnOff(savedOn)), cb.WithFixedWidth(30), name, 28);
            switchValues.Add((name, ShownOn(name, savedOn)));
        }

        /// <summary>A list of choices for the setting called <paramref name="name"/>; <paramref name="values"/> are what gets saved.</summary>
        private void Dropdown(string label, string description, string name, string[] values, string[] names, string savedValue)
        {
            var cb = Label(label, description);
            int index = Math.Max(0, Array.IndexOf(values, ShownValue(name, savedValue)));
            composer.AddDropDown(values, names, index, (code, selected) => { if (selected) Change(name, code, savedValue); }, cb, CairoFont.WhiteSmallText(), name);
        }

        /// <summary>A slider for a whole-number setting, showing each value as <paramref name="text"/> gives it.</summary>
        private void Slider(string label, string description, string name, int savedValue, int min, int max, int step, Func<int, string> text)
        {
            var cb = Label(label, description);
            string saved = savedValue.ToString(CultureInfo.InvariantCulture);
            composer.AddSlider(v => { Change(name, v.ToString(CultureInfo.InvariantCulture), saved); return true; }, cb.WithFixedHeight(20).WithFixedOffset(0, 4), name);
            int shown = int.TryParse(ShownValue(name, saved), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : savedValue;
            sliderValues.Add((name, shown, min, max, step, text));
        }

        // ---- The menu ----

        private void Compose()
        {
            var snap = core.ServerSettings;
            bool admin = snap?.IsAdmin == true;
            if (!admin) tab = YourSettingsTab;
            switchValues.Clear();
            sliderValues.Clear();
            rows = 0;
            y = 35;

            bg = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bg.BothSizing = ElementSizing.FitToChildren;
            var dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            composer = capi.Gui.CreateCompo("complexnutrition-settings", dialogBounds)
                .AddShadedDialogBG(bg)
                .AddDialogTitleBar(L("settings-title"), () => TryClose())
                .BeginChildElements(bg);

            if (admin)
            {
                var tabs = new[]
                {
                    new GuiTab { Name = L("settings-tab-yours"), DataInt = YourSettingsTab, Active = tab == YourSettingsTab },
                    new GuiTab { Name = L("settings-tab-world"), DataInt = WorldSettingsTab, Active = tab == WorldSettingsTab },
                };
                var tabBounds = ElementBounds.Fixed(0, y, FullWidth, 25);
                bg.WithChild(tabBounds);
                var font = CairoFont.WhiteSmallText().WithFontSize(17);
                var selected = CairoFont.WhiteSmallText().WithFontSize(17).WithColor(GuiStyle.ActiveButtonTextColor);
                composer.AddHorizontalTabs(tabs, tabBounds, OnTab, font, selected, "tabs");
                y += 40;
            }

            if (tab == WorldSettingsTab) ComposeWorldSettings(snap);
            else ComposeYourSettings(snap, admin);

            // Cancel on the left, Save on the right.
            var cancel = ElementBounds.Fixed(0, y + 12, 0, 0).WithFixedPadding(20, 4);
            var save = ElementBounds.Fixed(0, y + 12, 0, 0).WithAlignment(EnumDialogArea.RightFixed).WithFixedPadding(20, 4);
            bg.WithChildren(cancel, save);
            composer.AddSmallButton(L("settings-cancel"), () => { TryClose(); return true; }, cancel);
            composer.AddSmallButton(L("settings-save"), OnSave, save);

            SingleComposer = composer.EndChildElements().Compose();
            var tabElement = SingleComposer.GetHorizontalTabs("tabs");
            if (tabElement != null) tabElement.activeElement = tab;
            foreach (var (key, on) in switchValues) SingleComposer.GetSwitch(key)?.SetValue(on);
            foreach (var (key, value, min, max, step, text) in sliderValues)
            {
                var slider = SingleComposer.GetSlider(key);
                if (slider == null) continue;
                slider.OnSliderTooltip = v => text(v);
                slider.OnSliderRestingText = v => text(v);
                slider.ShowTextWhenResting = true;
                slider.SetValues(value, min, max, step);
            }
        }

        /// <summary>Settings every player has. They are kept for this player only and change nothing for anyone else.</summary>
        private void ComposeYourSettings(SettingsSnapshot snap, bool admin)
        {
            Heading(L("settings-cat-display"));

            // Until the player picks, the advanced display and amounts-or-percent follow the world's defaults.
            var state = NutritionState.Of(capi.World.Player?.Entity);
            int displayChoice = state?.DisplayChoice ?? 0;
            bool advancedSaved = displayChoice == 0 ? Digestion.WorldAdvancedDefault(capi.World) : displayChoice == 1;
            Switch(L("settings-advanced"), L("settings-advanced-desc"), SettingKeys.Display, advancedSaved);

            string unitsSaved = core.Client.DisplayUnits;
            if (unitsSaved != "amount" && unitsSaved != "percent") unitsSaved = snap?.ShowPercent == true ? "percent" : "amount";
            Dropdown(L("settings-units"), L("settings-units-desc"), UnitsChange,
                new[] { "amount", "percent" }, new[] { L("settings-units-amount"), L("settings-units-percent") }, unitsSaved);

            Switch(L("settings-short-tooltips"), L("settings-short-tooltips-desc"), ShortTooltipsChange, core.Client.ShortTooltips);
            Switch(L("settings-hud-icon"), L("settings-hud-icon-desc"), HudIconChange, core.Client.HudIcon);

            if (!admin)
            {
                y += 8;
                Note(L("settings-admin-note"), 40);
            }
        }

        /// <summary>Settings for the whole world. Only admins see this tab.</summary>
        private void ComposeWorldSettings(SettingsSnapshot snap)
        {
            Note(L("settings-world-note"), 24);

            Heading(L("settings-cat-foodinfo"));
            Switch(L("settings-tooltips"), L("settings-tooltips-desc"), SettingKeys.ShowTooltip, snap.ShowTooltip);
            Switch(L("settings-discover"), WorldDescription(WorldSettings.DiscoverKey), SettingKeys.Discover, snap.Discover);
            // A part of Discover Nutrition (v0.4.1), so it sits right under it and only works while that is on.
            Switch(L("settings-hidepoison"), WorldDescription(WorldSettings.HidePoisonKey), SettingKeys.HidePoison, snap.HidePoison);
            Switch(L("settings-logfallbacks"), L("settings-logfallbacks-desc"), SettingKeys.LogFallbacks, snap.LogFallbacks);

            // What a new character starts with and what dying does (v0.4.2): the calorie reserve, then the other stores.
            Heading(L("settings-cat-startdeath"));
            StartDropdown(L("settings-start"), WorldSettings.StartConditionKey, SettingKeys.StartCalories, snap.StartCalories ?? WorldSettings.DefaultStart);
            StartDropdown(L("settings-startstores"), WorldSettings.StartStoresKey, SettingKeys.StartStores, snap.StartStores ?? WorldSettings.DefaultStartStores);
            Dropdown(L("settings-respawn"), WorldDescription(WorldSettings.RespawnCaloriesKey), SettingKeys.RespawnCalories,
                WorldSettings.RespawnCaloriesOptions.Select(o => o.Value).ToArray(),
                WorldSettings.RespawnCaloriesOptions.Select(o => Lang.Get(o.LangKey(WorldSettings.RespawnCaloriesKey))).ToArray(),
                snap.RespawnCalories ?? WorldSettings.DefaultRespawnCalories);
            Switch(L("settings-keepstores"), WorldDescription(WorldSettings.KeepStoresKey), SettingKeys.KeepStores, snap.KeepStores);

            Heading(L("settings-cat-world"));
            Switch(L("settings-diminishing"), WorldDescription(WorldSettings.DiminishingReturnsKey), SettingKeys.DiminishingReturns, snap.DiminishingReturns);
            Dropdown(L("settings-wildharvest"), WorldDescription(WorldSettings.WildHarvestKey), SettingKeys.WildHarvest,
                WorldSettings.WildHarvestOptions,
                WorldSettings.WildHarvestOptions.Select(v => Lang.Get("worldconfig-" + WorldSettings.WildHarvestKey + "-" + v)).ToArray(),
                snap.WildHarvest ?? WorldSettings.DefaultWildHarvest);
            Slider(L("settings-maxfullness"), WorldDescription(WorldSettings.MaxFullnessKey), SettingKeys.MaxFullness,
                snap.MaxFullness, 0, WorldSettings.MaxFullnessTop, WorldSettings.MaxFullnessStep,
                v => v <= 0 ? L("settings-maxfullness-none") : L("settings-maxfullness-value", v));
            Switch(L("settings-meatinpots"), WorldDescription(WorldSettings.MeatInPotsKey), SettingKeys.MeatInPots, snap.MeatInPots);

            // ---- Nutrient Effects (v0.4.0): one switch for all, then one per nutrient in two columns ----
            Heading(L("settings-cat-effects"));
            Switch(L("settings-effects"), WorldDescription(WorldSettings.EffectsKey), SettingKeys.Effects, snap.Effects);
            var off = NutrientEffects.ParseOff(snap.EffectsOff);
            var withEffects = core.Nutrients.All.Where(n => NutrientEffects.Settings.WithEffects.Contains(n.Code)).ToList();
            const double half = FullWidth / 2;
            for (int i = 0; i < withEffects.Count; i++)
            {
                var n = withEffects[i];
                double x = i % 2 == 0 ? 0 : half;
                string nutrientName = Lang.Get(n.LangKey);
                var lb = ElementBounds.Fixed(x + 16, y + 6, half - 60, 24);
                bg.WithChild(lb);
                composer.AddStaticText(nutrientName, CairoFont.WhiteSmallText(), lb);
                composer.AddHoverText(L("settings-effect-one-desc", nutrientName, NutrientDisplay.EffectRule(n.Code, n)), CairoFont.WhiteSmallText(), 400, lb.FlatCopy());
                var cb = ElementBounds.Fixed(x + half - 44, y, 30, 28);
                bg.WithChild(cb);
                string name = SettingKeys.EffectPrefix + n.Code;
                string saved = OnOff(!off.Contains(n.Code));
                composer.AddSwitch(on => Change(name, OnOff(on), saved), cb, name, 28);
                switchValues.Add((name, ShownValue(name, saved) == "true"));
                if (i % 2 == 1 || i == withEffects.Count - 1)
                {
                    y += RowHeight;
                    rows++;
                }
            }

            Heading(L("settings-cat-modules"));
            Switch(L("settings-module-fooddata"), L("settings-module-fooddata-desc"), SettingKeys.ModulePrefix + ModuleIds.FoodData, ModuleOn(snap, ModuleIds.FoodData));
            Switch(L("settings-module-tracking"), L("settings-module-tracking-desc"), SettingKeys.ModulePrefix + ModuleIds.Tracking, ModuleOn(snap, ModuleIds.Tracking));
        }

        /// <summary>Starting calories and Starting vitamin and mineral reserves share the same six choices.</summary>
        private void StartDropdown(string label, string worldKey, string name, string savedValue)
        {
            Dropdown(label, WorldDescription(worldKey), name,
                WorldSettings.StartOptions.Select(o => o.Value).ToArray(),
                WorldSettings.StartOptions.Select(o => Lang.Get(o.LangKey(worldKey))).ToArray(),
                savedValue);
        }

        /// <summary>A world setting's hover is the same text the world creation screen shows for it.</summary>
        private static string WorldDescription(string worldKey) => Lang.Get("worldattribute-" + worldKey + "-desc");

        private static bool ModuleOn(SettingsSnapshot snap, string id) => snap?.Modules != null && snap.Modules.TryGetValue(id, out var on) && on;

        private void OnTab(int index)
        {
            tab = index;
            Compose();
        }

        // ---- Saving ----

        /// <summary>Sends every waiting change and closes the menu. Switching tracking asks first, since it changes everyone.</summary>
        private bool OnSave()
        {
            var changes = new Dictionary<string, string>(unsavedChanges);
            unsavedChanges.Clear();

            string trackingKey = SettingKeys.ModulePrefix + ModuleIds.Tracking;
            bool trackingChanged = changes.TryGetValue(trackingKey, out string trackingValue);
            changes.Remove(trackingKey);

            foreach (var (name, value) in changes)
            {
                switch (name)
                {
                    case UnitsChange: core.SetClientDisplayUnits(value); break;
                    case ShortTooltipsChange: core.SetClientShortTooltips(value == "true"); break;
                    case HudIconChange: core.SetClientHudIcon(value == "true"); break;
                    case SettingKeys.Display: core.SendSetting(SettingKeys.Display, value == "true" ? "1" : "2"); break;
                    default: core.SendSetting(name, value); break;
                }
            }

            TryClose();
            if (trackingChanged) ConfirmTracking(trackingValue == "true");
            return true;
        }

        /// <summary>Switching tracking converts every player's state, so it asks first and says what will happen.</summary>
        private void ConfirmTracking(bool on)
        {
            // Our Stats panel only comes back once the character window is reopened, so the box says so.
            new GuiDialogConfirmTip(capi, L(on ? "settings-confirm-on" : "settings-confirm-off"), on ? L("settings-confirm-on-tip") : null, ok =>
            {
                if (ok) core.SendSetting(SettingKeys.ModulePrefix + ModuleIds.Tracking, OnOff(on));
            }).TryOpen();
        }

        /// <summary>New settings from the server: redraw if open. Unsaved changes stay as they are.</summary>
        public void Refresh()
        {
            if (IsOpened()) Compose();
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            unsavedChanges.Clear();
            Compose();
        }

        /// <summary>Closing without Save (Cancel or the close button) drops every unsaved change.</summary>
        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            unsavedChanges.Clear();
        }
    }
}
