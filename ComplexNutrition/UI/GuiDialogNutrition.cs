using System;
using System.Collections.Generic;
using ComplexNutrition.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace ComplexNutrition.UI
{
    /// <summary>
    /// The nutrition window (Left Alt + N, or the button on the Stats panel): the same size as the handbook and
    /// placed like it, with tabs along the top and a Settings button. The open page refreshes every second; the
    /// history (History tab and trend arrows) is asked from the server on opening and every half minute after.
    /// </summary>
    public sealed class GuiDialogNutrition : GuiDialog
    {
        public const double ContentWidth = 500;
        public const double ContentHeight = 500;

        private readonly ComplexNutritionModSystem core;
        private GuiElementNutritionPage page;
        private int tab;
        private long ticker;
        private int ticks;

        public const int HistoryTab = 4;
        /// <summary>Seconds between history requests while the window is open.</summary>
        public const int HistorySeconds = 30;

        private static readonly string[] TabKeys = { "window-tab-overview", "window-tab-digestion", "window-tab-vitamins", "window-tab-minerals", "window-tab-history" };

        public GuiDialogNutrition(ICoreClientAPI capi, ComplexNutritionModSystem core) : base(capi)
        {
            this.core = core;
        }

        /// <summary>The player's choice (since v0.3.5): pause a single player game while the window is open, like the handbook. Off by default.</summary>
        public const string PauseSettingKey = "complexnutritionWindowPause";

        private bool pausedByUs;

        private bool CanPause => capi.IsSinglePlayer && !capi.OpenedToLan;

        private bool PauseChosen => capi.Settings.Bool.Get(PauseSettingKey, false);

        public override string ToggleKeyCombinationCode => null;

        public override bool PrefersUngrabbedMouse => true;

        private void Compose()
        {
            var tabs = new GuiTab[TabKeys.Length];
            for (int i = 0; i < tabs.Length; i++) tabs[i] = new GuiTab { Name = Lang.Get("complexnutrition:" + TabKeys[i]), DataInt = i, Active = i == tab };

            ElementBounds tabBounds = ElementBounds.Fixed(0.0, 35.0, ContentWidth, 25.0);
            ElementBounds pageBounds = ElementBounds.Fixed(3.0, 75.0, ContentWidth, ContentHeight);
            ElementBounds clipBounds = pageBounds.ForkBoundingParent();
            ElementBounds insetBounds = pageBounds.FlatCopy().FixedGrow(6.0).WithFixedOffset(-3.0, -3.0);
            ElementBounds scrollBounds = insetBounds.CopyOffsetedSibling(3.0 + ContentWidth + 7.0).WithFixedWidth(20.0);
            ElementBounds closeBounds = ElementBounds.FixedSize(0.0, 0.0).FixedUnder(clipBounds, 15.0)
                .WithAlignment(EnumDialogArea.RightFixed).WithFixedPadding(20.0, 4.0).WithFixedAlignmentOffset(-6.0, 0.0);
            ElementBounds settingsBounds = ElementBounds.FixedSize(0.0, 0.0).FixedUnder(clipBounds, 15.0).WithFixedPadding(20.0, 4.0);

            // In the title bar, just left of its menu and close icons and in line with them.
            ElementBounds pauseBounds = ElementBounds.Fixed(0.0, 4.5, 100.0, 22.0)
                .WithAlignment(EnumDialogArea.RightFixed).WithFixedAlignmentOffset(-62.0, 0.0);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(tabBounds, settingsBounds, insetBounds, clipBounds, scrollBounds, closeBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.CenterFixed).WithFixedPosition(0.0, 70.0);

            page = new GuiElementNutritionPage(capi, pageBounds, ContentWidth - 4.0);
            var font = CairoFont.WhiteSmallText().WithFontSize(17);
            var selected = CairoFont.WhiteSmallText().WithFontSize(17).WithColor(GuiStyle.ActiveButtonTextColor);

            var composer = capi.Gui.CreateCompo("complexnutrition-window", dialogBounds)
                .AddShadedDialogBG(bgBounds, true, 5.0, 0.75f)
                .AddDialogTitleBar(Lang.Get("complexnutrition:window-title"), OnTitleBarClose)
                .AddIf(CanPause)
                    .AddToggleButton(Lang.Get("Pause game"), CairoFont.WhiteDetailText(), OnTogglePause, pauseBounds, "pausegame")
                .EndIf()
                .BeginChildElements(bgBounds)
                    .AddHorizontalTabs(tabs, tabBounds, OnTab, font, selected, "tabs")
                    .AddInset(insetBounds, 3, 0.85f)
                    .BeginClip(clipBounds)
                        .AddInteractiveElement(page, "page")
                    .EndClip()
                    .AddVerticalScrollbar(OnScroll, scrollBounds, "scrollbar")
                    .AddSmallButton(Lang.Get("complexnutrition:window-settings"), OnSettings, settingsBounds, EnumButtonStyle.Normal, "settings")
                    .AddSmallButton(Lang.Get("complexnutrition:window-close"), () => { TryClose(); return true; }, closeBounds, EnumButtonStyle.Normal);
            SingleComposer = composer.EndChildElements().Compose();
            SingleComposer.GetHorizontalTabs("tabs").activeElement = tab;
            SingleComposer.GetToggleButton("pausegame")?.SetValue(PauseChosen);
            Refresh(true);
        }

        private void OnTab(int index)
        {
            tab = index;
            if (index == HistoryTab) core.RequestHistory();
            if (page != null) page.ScrollOffset = 0;
            var bar = SingleComposer?.GetScrollbar("scrollbar");
            if (bar != null) bar.CurrentYPosition = 0;
            Refresh(true);
        }

        private void OnTogglePause(bool on)
        {
            capi.Settings.Bool[PauseSettingKey] = on;
            if (!CanPause) return;
            capi.PauseGame(on);
            pausedByUs = on;
        }

        private bool OnSettings()
        {
            core.OpenSettings();
            return true;
        }

        private void OnScroll(float value)
        {
            if (page != null) page.ScrollOffset = value;
        }

        /// <summary>Rebuilds the open page from the player's current state; redraws only if something changed.</summary>
        public void Refresh(bool force = false)
        {
            if (page == null || SingleComposer == null) return;
            var data = core.BuildPageData();
            List<PageBlock> blocks = tab switch
            {
                1 => NutritionPages.DigestionPage(data),
                2 => NutritionPages.Vitamins(data),
                3 => NutritionPages.Minerals(data),
                HistoryTab => NutritionPages.History(data),
                _ => NutritionPages.Overview(data),
            };
            if (page.SetBlocks(blocks) || force)
            {
                var bar = SingleComposer.GetScrollbar("scrollbar");
                bar?.SetHeights((float)ContentHeight, (float)Math.Max(ContentHeight, page.ContentHeight));
                if (bar != null && page.ScrollOffset > Math.Max(0, page.ContentHeight - ContentHeight))
                {
                    page.ScrollOffset = Math.Max(0, page.ContentHeight - ContentHeight);
                    bar.CurrentYPosition = (float)page.ScrollOffset;
                }
            }
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            Compose();
            if (CanPause && PauseChosen)
            {
                capi.PauseGame(true);
                pausedByUs = true;
            }
            core.RequestHistory();
            ticks = 0;
            ticker = capi.Event.RegisterGameTickListener(_ =>
            {
                if (++ticks % HistorySeconds == 0) core.RequestHistory();
                Refresh();
            }, 1000);
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            if (pausedByUs) capi.PauseGame(false);
            pausedByUs = false;
            if (ticker != 0) capi.Event.UnregisterGameTickListener(ticker);
            ticker = 0;
        }

        public override void OnRenderGUI(float deltaTime)
        {
            base.OnRenderGUI(deltaTime);
            page?.RenderTooltip();
        }

        private void OnTitleBarClose() => TryClose();
    }
}
