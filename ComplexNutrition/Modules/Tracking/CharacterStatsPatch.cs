using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using ComplexNutrition.Core;
using ComplexNutrition.UI;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace ComplexNutrition.Modules.Tracking
{
    /// <summary>
    /// Client: while the server tracks this player, the Stats panel next to the character dialog shows one
    /// bar per nutrient reserve (and per gut pool) in place of vanilla's five food category bars, and the HUD
    /// bar above the hotbar shows the calorie reserve in place of satiety. Everything else on the panel
    /// is laid out as vanilla does it (CharacterExtraDialogs.ComposeStatsGui in 1.22.7). When the server does
    /// not track the player, vanilla's panel is left alone.
    /// </summary>
    public static class CharacterStatsPatch
    {
        private const string HarmonyId = "complexnutrition.tracking.client";
        private const double RowHeight = 17;

        private static Harmony harmony;
        private static ComplexNutritionModSystem core;
        private static ICoreClientAPI capi;
        private static bool listening;

        private static readonly AccessTools.FieldRef<CharacterExtraDialogs, GuiDialogCharacterBase> DlgRef =
            AccessTools.FieldRefAccess<CharacterExtraDialogs, GuiDialogCharacterBase>("dlg");

        public static void Apply(ICoreClientAPI api, ComplexNutritionModSystem system)
        {
            capi = api;
            core = system;
            if (harmony == null)
            {
                harmony = new Harmony(HarmonyId);
                harmony.Patch(AccessTools.Method(typeof(CharacterExtraDialogs), nameof(CharacterExtraDialogs.ComposeStatsGui)),
                    prefix: new HarmonyMethod(typeof(CharacterStatsPatch), nameof(ComposeStatsPrefix)));
                harmony.Patch(AccessTools.Method(typeof(CharacterExtraDialogs), "UpdateStatBars"),
                    prefix: new HarmonyMethod(typeof(CharacterStatsPatch), nameof(UpdateStatBarsPrefix)));

                // The HUD bar above the hotbar: calories in place of satiety (an internal class, so looked up by name).
                hudType = AccessTools.TypeByName("Vintagestory.Client.NoObf.HudStatbar");
                var update = hudType == null ? null : AccessTools.Method(hudType, "UpdateSaturation");
                var flash = hudType == null ? null : AccessTools.Method(hudType, "OnFlashStatbars");
                hudBarField = hudType == null ? null : AccessTools.Field(hudType, "saturationbar");
                hudLastField = hudType == null ? null : AccessTools.Field(hudType, "lastSaturation");
                if (update != null && flash != null && hudBarField != null)
                {
                    harmony.Patch(update, prefix: new HarmonyMethod(typeof(CharacterStatsPatch), nameof(HudUpdatePrefix)));
                    harmony.Patch(flash, postfix: new HarmonyMethod(typeof(CharacterStatsPatch), nameof(HudFlashPostfix)));
                }
                else
                {
                    capi.Logger.Warning("[complexnutrition] Could not find the HUD satiety bar; it keeps showing fullness instead of calories.");
                }
            }
            if (!listening)
            {
                listening = true;
                capi.Event.LevelFinalize += ListenForChanges;
                // Switched on while the game runs (from the settings menu): the level is already loaded.
                if (capi.World?.Player?.Entity != null) ListenForChanges();
                hoverListener = capi.Event.RegisterGameTickListener(OnHoverTick, 100);
                barListener = capi.Event.RegisterGameTickListener(ApplyPendingBars, 40);
                hoverBox = new StatsHover(capi, UpdateHover);
                capi.Event.RegisterRenderer(hoverBox, EnumRenderStage.Ortho, "complexnutrition-statshover");
            }
        }

        private static Type hudType;
        private static System.Reflection.FieldInfo hudBarField;
        private static System.Reflection.FieldInfo hudLastField;
        private static GuiElementStatbar hudBar;
        private static float hudKcal = -1f, hudFullness = -1f, hudMax = -1f;
        private static bool hudOurs;

        /// <summary>The calorie reserve in kcal and its size, or false while the server does not track this player.</summary>
        private static bool CalorieReserve(out float kcal, out float maxKcal, out float fullness, out float maxFullness)
        {
            kcal = maxKcal = fullness = maxFullness = 0f;
            var attrs = capi?.World?.Player?.Entity?.WatchedAttributes;
            var tree = attrs?.GetTreeAttribute(NutritionState.TreeKey);
            var def = core?.Nutrients.Get(NutrientCodes.Calories);
            if (tree == null || def == null || !NutritionState.FromTree(tree).TrackingOn) return false;
            var reserves = tree.GetTreeAttribute("reserves");
            kcal = (reserves?.GetFloat(def.Code) ?? 0f) / 100f * def.DailyTarget;
            maxKcal = Math.Max(def.Cap / 100f * def.DailyTarget, kcal);
            var hunger = attrs.GetTreeAttribute("hunger");
            fullness = hunger?.GetFloat("currentsaturation") ?? 0f;
            maxFullness = hunger?.GetFloat("maxsaturation") ?? 0f;
            return true;
        }

        /// <summary>
        /// Vanilla redraws the satiety bar every 20 ms when the value changed. While tracking runs, it shows the
        /// calorie reserve instead, one block per active day, with fullness in its hover text.
        /// </summary>
        public static bool HudUpdatePrefix(object __instance)
        {
            var bar = hudBarField?.GetValue(__instance) as GuiElementStatbar;
            if (!CalorieReserve(out var kcal, out var maxKcal, out var fullness, out var maxFullness))
            {
                if (hudOurs && bar != null)
                {
                    // Tracking stopped: hand the bar back to vanilla and make it redraw.
                    bar.ShowValueOnHover = true;
                    hudLastField?.SetValue(__instance, -1f);
                    hudOurs = false;
                    hudBar = null;
                }
                return true;
            }
            if (bar == null) return false;
            if (bar != hudBar)
            {
                hudBar = bar;
                hudKcal = -1f;
                bar.ShowValueOnHover = false;   // StatsHover draws its hover instead
            }
            hudOurs = true;
            if (Math.Abs(kcal - hudKcal) < 1f && Math.Abs(fullness - hudFullness) < 0.5f && maxKcal == hudMax) return false;
            hudKcal = kcal;
            hudFullness = fullness;
            hudMax = maxKcal;
            var def = core.Nutrients.Get(NutrientCodes.Calories);
            bar.SetLineInterval(def.DailyTarget);   // one block per active day
            bar.SetValues(kcal, 0f, maxKcal);
            return false;
        }

        private static string HudHover()
        {
            if (!CalorieReserve(out var kcal, out var maxKcal, out var fullness, out var maxFullness)) return "";
            var def = core.Nutrients.Get(NutrientCodes.Calories);
            float pct = def.ToPercent(kcal);
            return new StatsHover.Builder(Lang.Get(def.LangKey), StatsHover.Hex(NutrientDisplay.GroupColors["Energy"]))
                .Section(Lang.Get("complexnutrition:hover-status"),
                    StatsHover.Vtml("complexnutrition:hud-calories", Amount(def, pct), StatsHover.Text(NutrientDisplay.DaysText(pct))),
                    StatsHover.Text(Lang.Get("complexnutrition:fullness-hover", (int)Math.Round(fullness), (int)Math.Round(maxFullness))))
                .ToString();
        }

        /// <summary>Vanilla flashes the bar below 20% satiety; while tracking runs it flashes below 10% of a full calorie reserve instead.</summary>
        public static void HudFlashPostfix(object __instance)
        {
            if (!CalorieReserve(out var kcal, out var maxKcal, out _, out _)) return;
            if (hudBarField?.GetValue(__instance) is not GuiElementStatbar bar) return;
            var mode = capi.World.Player.WorldData.CurrentGameMode;
            float below = Digestion.Settings?.FlashBelow ?? 0.1f;
            bar.ShouldFlash = mode != EnumGameMode.Creative && mode != EnumGameMode.Spectator && maxKcal > 0f && kcal / maxKcal < below;
        }

        public static void Remove()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            // With our patches gone, put vanilla's Stats panel back so vanilla's own updates find its bars.
            try
            {
                if (capi != null) RecomposeVanilla(capi.ModLoader.GetModSystem<CharacterExtraDialogs>());
            }
            catch (Exception e)
            {
                capi?.Logger.Warning("[complexnutrition] Could not restore the vanilla Stats panel: {0}", e.Message);
            }
            if (capi != null && listening)
            {
                capi.Event.LevelFinalize -= ListenForChanges;
                capi.Event.UnregisterGameTickListener(hoverListener);
                capi.Event.UnregisterGameTickListener(barListener);
                if (hoverBox != null) capi.Event.UnregisterRenderer(hoverBox, EnumRenderStage.Ortho);
            }
            hoverBox?.Dispose();
            hoverBox = null;
            dialogs = null;
            statHovers.Clear();
            barValues.Clear();
            pendingBars.Clear();
            hoverBar = null;
            hoverFunc = null;
            if (hudBar != null)
            {
                hudBar.ShowValueOnHover = true;
            }
            hudBar = null;
            hudOurs = false;
            listening = false;
            capi = null;
            core = null;
        }

        private static object listenedEntity;

        private static void ListenForChanges()
        {
            var entity = capi?.World?.Player?.Entity;
            if (entity == null || ReferenceEquals(entity, listenedEntity)) return;   // one listener per player entity, however often tracking is switched on
            listenedEntity = entity;
            entity.WatchedAttributes.RegisterModifiedListener(NutritionState.TreeKey, () =>
            {
                if (capi == null) return;
                var dialogs = capi.ModLoader.GetModSystem<CharacterExtraDialogs>();
                if (dialogs == null || DlgRef(dialogs)?.IsOpened() != true) return;
                UpdateBars(DlgRef(dialogs).Composers["playerstats"]);
            });
        }

        private static NutritionState TrackedState()
        {
            var state = NutritionState.Of(capi?.World?.Player?.Entity);
            return state != null && state.TrackingOn && core != null && core.Nutrients.All.Count > 0 ? state : null;
        }

        /// <summary>True while the Stats panel holds our bars rather than vanilla's five food bars.</summary>
        private static bool IsOurPanel(GuiComposer composer) =>
            composer != null && core != null && core.Nutrients.All.Any(n => composer.GetStatbar("cn-" + n.Code) != null);

        /// <summary>
        /// Tracking stopped while our panel is in place (the server's update can arrive before the module switch):
        /// vanilla's UpdateStatBars would look for its food bars there and crash, so recompose the panel the vanilla
        /// way first. Returns false when there is no panel to fix.
        /// </summary>
        private static bool RecomposeVanilla(CharacterExtraDialogs dialogs)
        {
            var dlg = dialogs == null ? null : DlgRef(dialogs);
            var composer = dlg?.Composers["playerstats"];
            if (!IsOurPanel(composer)) return false;
            statHovers.Clear();
            barValues.Clear();
            pendingBars.Clear();
            hoverBar = null;
            hoverFunc = null;
            if (dlg.Composers["playercharacter"] == null || dlg.Composers["environment"] == null)
            {
                // Not composed yet: drop ours, vanilla composes its own when the dialog next opens.
                dlg.Composers.Remove("playerstats");
                return true;
            }
            dialogs.ComposeStatsGui();   // our prefix lets vanilla through now tracking is off
            return true;
        }

        public static bool UpdateStatBarsPrefix(CharacterExtraDialogs __instance)
        {
            if (TrackedState() == null)
            {
                // ComposeStatsGui ends by calling UpdateStatBars itself, on vanilla's bars.
                if (RecomposeVanilla(__instance)) return false;
                return true;
            }
            var dlg = DlgRef(__instance);
            var composer = dlg?.Composers["playerstats"];
            if (composer == null || !dlg.IsOpened()) return false;
            if (!IsOurPanel(composer))
            {
                // Tracking started while vanilla's panel was open: swap ours in.
                __instance.ComposeStatsGui();
                return false;
            }

            var hunger = capi.World.Player.Entity.WatchedAttributes.GetTreeAttribute("hunger");
            if (hunger != null)
            {
                composer.GetDynamicText("satiety")?.SetNewText((int)Math.Round(hunger.GetFloat("currentsaturation")) + " / " + (int)Math.Round(hunger.GetFloat("maxsaturation")));
            }
            UpdateBars(composer);
            return false;
        }

        private static void UpdateBars(GuiComposer composer, bool now = false)
        {
            var state = TrackedState();
            if (composer == null || state == null) return;
            foreach (var n in core.Nutrients.All)
            {
                var bar = composer.GetStatbar("cn-" + n.Code);
                if (bar == null) continue;
                float reserve = state.GetReserve(n.Code);
                float max = Math.Max(n.Cap, reserve);
                bar.SetLineInterval(100);   // one block per day of storage, or per day's need for a gut pool
                SetBar(bar, reserve, max, now);
            }
        }

        /// <summary>
        /// A Stats bar's hover: the row's name as a title in the bar's colour, a Status section with the given lines,
        /// the nutrient's role under About, then its "-effect" text under In Game when it has one (empty = none, for
        /// nutrients whose description already says what they do), or the "planned" note in grey when it has none.
        /// </summary>
        private static string NutrientHover(string title, double[] color, string code, params string[] status)
        {
            var b = new StatsHover.Builder(title, StatsHover.Hex(color))
                .Section(Lang.Get("complexnutrition:hover-status"), status)
                .Section(Lang.Get("complexnutrition:hover-about"), StatsHover.Text(Lang.Get("complexnutrition:nutrient-" + code + "-role")));
            string key = "complexnutrition:nutrient-" + code + "-effect";
            if (!Lang.HasTranslation(key)) b.Note(Lang.Get("complexnutrition:effect-placeholder"));
            else b.Section(Lang.Get("complexnutrition:hover-ingame"), StatsHover.Text(Lang.Get(key)));
            return b.ToString();
        }

        // Vanilla redraws a bar's hover text into a texture every time the bar's value is set, whether or not the
        // mouse is over it. With fifteen bars and long descriptions that stalled the game each time the reserves
        // changed (every few seconds, and several times per food eaten). So our bars turn vanilla's hover off and
        // StatsHover draws one box for the bar under the mouse: its text is built when the mouse arrives and every
        // 100 ms after, and the box is only redrawn when that text changes. Only bars that visibly moved are set.
        private static readonly Dictionary<GuiElementStatbar, System.Func<string>> statHovers = new();
        private static readonly Dictionary<GuiElementStatbar, float[]> barValues = new();
        private static StatsHover hoverBox;
        private static CharacterExtraDialogs dialogs;
        private static GuiElementStatbar hoverBar;
        private static System.Func<string> hoverFunc;
        private static long hoverListener;

        private static bool MouseOver(GuiElementStatbar bar) =>
            capi != null && bar?.Bounds != null && bar.Bounds.PointInside(capi.Input.MouseX, capi.Input.MouseY);

        // Each SetValues redraws and blurs the bar on a worker thread and uploads two textures on the main thread.
        // Digestion moves several bars every update (and the hunger and nutrition trees both report each update),
        // so setting them all at once gave a hitch every few seconds. Changes are queued instead, the newest value
        // per bar wins, and a few are applied every 40 ms.
        private static readonly Dictionary<GuiElementStatbar, float[]> pendingBars = new();
        private static long barListener;
        private const int BarsPerStep = 2;

        /// <summary>Queues a Stats bar's new value unless it would move by less than a pixel.</summary>
        private static void SetBar(GuiElementStatbar bar, float value, float max, bool now)
        {
            if (now)
            {
                // A freshly composed panel: draw every bar straight away.
                pendingBars.Remove(bar);
                barValues[bar] = new[] { value, max };
                bar.SetValues(value, 0f, max);
                return;
            }
            double px = Math.Max(20.0, bar.Bounds?.InnerWidth ?? 0.0);   // the bar's width on screen
            if (barValues.TryGetValue(bar, out var last) && last[1] == max && max > 0f && Math.Abs(last[0] - value) / max * px < 1.0)
            {
                pendingBars.Remove(bar);
                return;
            }
            pendingBars[bar] = new[] { value, max };
        }

        private static void ApplyPendingBars(float dt)
        {
            if (pendingBars.Count == 0) return;
            int done = 0;
            foreach (var bar in pendingBars.Keys.ToArray())
            {
                if (done++ >= BarsPerStep) break;
                var v = pendingBars[bar];
                pendingBars.Remove(bar);
                barValues[bar] = v;
                bar.SetValues(v[0], 0f, v[1]);
            }
        }

        /// <summary>Our bar under the mouse (the HUD bar, or a Stats bar while the panel is open), with its hover text.</summary>
        private static GuiElementStatbar HoveredBar(out System.Func<string> text)
        {
            text = null;
            if (capi == null || capi.Input.MouseGrabbed) return null;
            if (hudOurs && hudBar != null && MouseOver(hudBar))
            {
                text = HudHover;
                return hudBar;
            }
            if (statHovers.Count == 0) return null;
            dialogs ??= capi.ModLoader.GetModSystem<CharacterExtraDialogs>();
            if (dialogs == null || DlgRef(dialogs)?.IsOpened() != true) return null;
            foreach (var kv in statHovers)
            {
                if (!MouseOver(kv.Key)) continue;
                text = kv.Value;
                return kv.Key;
            }
            return null;
        }

        /// <summary>Every frame: shows the box for the bar the mouse just moved onto, or hides it.</summary>
        private static void UpdateHover()
        {
            var bar = HoveredBar(out var text);
            if (bar == null)
            {
                hoverBar = null;
                hoverFunc = null;
                hoverBox?.Hide();
                return;
            }
            if (bar == hoverBar) return;
            hoverBar = bar;
            hoverFunc = text;
            hoverBox?.Show(text());
        }

        /// <summary>Every 100 ms: refreshes the shown box's text (redrawn only if it changed).</summary>
        private static void OnHoverTick(float dt)
        {
            if (hoverBar != null && hoverFunc != null) hoverBox?.Show(hoverFunc());
        }

        /// <summary>An amount as hover VTML, in its unit's colour.</summary>
        private static string Amount(NutrientDefinition def, float percentOfDaily) =>
            StatsHover.Text(def.Format(percentOfDaily, core.ShowPercent));

        /// <summary>
        /// A gut pool's first hover line (VTML). With the advanced nutrition display: grams, the calories they will give
        /// (after diminishing returns), time to absorb and the current absorption rate; otherwise grams and time.
        /// </summary>
        private static string GutLine(NutritionState st, NutrientDefinition def, float reserve)
        {
            string code = def.Code;
            string hours = NutrientDisplay.GutHoursText(st?.GetGutHours(code) ?? 0f, reserve);
            var s = Digestion.Settings;
            var pool = s?.Pool(code);
            if (st == null || pool == null || !Digestion.AdvancedDisplay(capi?.World, st) || reserve <= 0.01f)
            {
                return StatsHover.Vtml("complexnutrition:gut-hover", Amount(def, reserve), StatsHover.Text(hours));
            }
            float grams = reserve / 100f * def.DailyTarget;
            float window = Digestion.WindowGrams(st, code, pool);
            float kcal = pool.Effective(window, grams) * pool.KcalPerGram;
            float rate = pool.Rate(grams, Digestion.FiberSpeed(s, st, pool));
            float kcalRate = pool.Effective(window, rate) * pool.KcalPerGram;
            return StatsHover.Vtml("complexnutrition:gut-hover-advanced",
                Amount(def, reserve),
                StatsHover.Text(Kcal(kcal)),
                StatsHover.Text(hours),
                StatsHover.Paint(Lang.Get("complexnutrition:gut-rate", def.FormatAmount(rate, core.ShowPercent), Kcal(kcalRate)), UnitColors.Rate));
        }

        private static string Num(float v) => Fmt.Number(v);

        /// <summary>Fiber's hover line: the calories per hour it adds right now, its rate in red like the gut lines.</summary>
        private static string FiberBoostLine(float kcalPerHour) => kcalPerHour < 0.05f
            ? StatsHover.Text(Lang.Get("complexnutrition:fiber-boost-none"))
            : StatsHover.Vtml("complexnutrition:fiber-boost", StatsHover.Paint(Kcal(kcalPerHour), UnitColors.Rate));

        /// <summary>Calories as text, as an amount or in % of a day's need, as the player chose.</summary>
        private static string Kcal(float kcal) => core.Nutrients.Get(NutrientCodes.Calories)?.FormatAmount(kcal, core.ShowPercent) ?? Num(kcal) + "kcal";

        private static bool IsGutPool(string code) => Digestion.Settings?.Pool(code) != null;

        /// <summary>Vanilla's ComposeStatsGui with the nutrition block swapped for our reserve bars.</summary>
        public static bool ComposeStatsPrefix(CharacterExtraDialogs __instance)
        {
            var state = TrackedState();
            if (state == null) return true;
            var dlg = DlgRef(__instance);
            if (dlg == null) return true;
            var Composers = dlg.Composers;
            var nutrients = core.Nutrients.All;
            statHovers.Clear();
            barValues.Clear();
            pendingBars.Clear();
            hoverBar = null;
            hoverFunc = null;

            ElementBounds bounds = Composers["playercharacter"].Bounds;
            ElementBounds envBounds = Composers["environment"].Bounds;
            ElementBounds leftColumnBoundsW = ElementBounds.Fixed(0.0, 0.0, 140.0, 20.0);
            ElementBounds rightColumnBounds = ElementBounds.Fixed(165.0, 0.0, 120.0, 20.0);
            EntityPlayer entity = capi.World.Player.Entity;

            // Vanilla sizes the panel to the character and environment dialogs next to it. Our 14 rows
            // usually fit in that; grow the panel only if they don't (header, rows, Physical block of up to 10 lines).
            double envHeight = envBounds.InnerHeight / RuntimeEnv.GUIScale + 10.0;
            double vanillaHeight = bounds.InnerHeight / RuntimeEnv.GUIScale - GuiStyle.ElementToDialogPadding - 20.0 + envHeight;
            double neededHeight = 45.0 + nutrients.Count * RowHeight + 28.0 + 10 * 20.0 + 10.0;
            ElementBounds bgBounds = ElementBounds.Fixed(0.0, 0.0, 235.0, Math.Max(vanillaHeight, neededHeight))
                .WithFixedPadding(GuiStyle.ElementToDialogPadding);
            ElementBounds dialogBounds = bgBounds.ForkBoundingParent().WithAlignment(EnumDialogArea.LeftMiddle)
                .WithFixedAlignmentOffset((bounds.renderX + bounds.OuterWidth + 10.0) / RuntimeEnv.GUIScale, envHeight / 2.0);

            GetHealthSat(out var health, out var maxHealth, out var saturation, out var maxSaturation);
            float walkspeed = entity.Stats.GetBlended("walkspeed");
            float healing = entity.Stats.GetBlended("healingeffectivness");
            float hungerrate = entity.Stats.GetBlended("hungerrate");
            float rangedAcc = entity.Stats.GetBlended("rangedWeaponsAcc");
            float rangedSpeed = entity.Stats.GetBlended("rangedWeaponsSpeed");
            ITreeAttribute bodyTemp = entity.WatchedAttributes.GetTreeAttribute("bodyTemp");
            float wetness = entity.WatchedAttributes.GetFloat("wetness");
            string wetText = wetness > 0.7 ? Lang.Get("wetness_soakingwet") : wetness > 0.4 ? Lang.Get("wetness_wet") : wetness > 0.1 ? Lang.Get("wetness_slightlywet") : "";

            var compo = Composers["playerstats"] = capi.Gui.CreateCompo("playerstats", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(Lang.Get("Stats"), () => dlg.OnTitleBarClose())
                .BeginChildElements(bgBounds);

            ElementBounds labelBounds = ElementBounds.Fixed(0.0, 25.0, 200.0, 20.0);
            compo.AddStaticText(Lang.Get("complexnutrition:stats-heading"), CairoFont.WhiteSmallText().WithWeight(FontWeight.Bold), labelBounds);
            // Opens the nutrition window (also Left Alt + N).
            compo.AddSmallButton(Lang.Get("complexnutrition:stats-open-window"), () => { core?.ToggleWindow(); return true; },
                ElementBounds.Fixed(172.0, 24.0, 0.0, 0.0).WithFixedPadding(5.0, 1.0), EnumButtonStyle.Small, "cn-openwindow");
            labelBounds = labelBounds.BelowCopy().WithFixedWidth(118.0).WithFixedHeight(RowHeight);
            // Calories, carbs and fats (in the gut), protein (in the gut and over the last 24 hours), then the stored nutrients.
            bool firstRow = true;
            void Row(string label, string barCode, double[] color, System.Func<string> hover)
            {
                if (!firstRow) labelBounds = labelBounds.BelowCopy();
                firstRow = false;
                var barBounds = ElementBounds.Fixed(120.0, labelBounds.fixedY + (RowHeight - 8.0) / 2.0 + 1.0, 112.0, 8.0);
                compo.AddStaticText(label, CairoFont.WhiteDetailText().WithFontSize(13), labelBounds)
                     .AddStatbar(barBounds, color, barCode);
                var statbar = compo.GetStatbar(barCode);
                statbar.ShowValueOnHover = false;   // StatsHover draws its hover instead
                statHovers[statbar] = hover;
            }

            for (int i = 0; i < nutrients.Count; i++)
            {
                var n = nutrients[i];
                var color = NutrientDisplay.ColorOf(n);
                var code = n.Code;
                var def = n;
                if (code == NutrientCodes.Protein && IsGutPool(code))
                {
                    // The bar shows the protein in the gut, like carbs and fats; the 24-hour total is in the hover.
                    string title = Lang.Get(n.LangKey);
                    Row(title, "cn-" + code, color, () =>
                    {
                        var st = NutritionState.Of(capi?.World?.Player?.Entity);
                        float r = st?.GetReserve(code) ?? 0f;
                        return NutrientHover(title, color, code, GutLine(st, def, r),
                            StatsHover.Text(Lang.Get("complexnutrition:protein24-hover", def.FormatAmount(st?.Protein24 ?? 0f, core.ShowPercent),
                                def.FormatAmount(Digestion.Settings?.ProteinNeedGrams ?? 64f, core.ShowPercent), def.FormatAmount(Digestion.Settings?.ProteinMaxGrams ?? 128f, core.ShowPercent))));
                    });
                }
                else if (IsGutPool(code))
                {
                    string title = Lang.Get("complexnutrition:stats-gut-" + code);
                    Row(title, "cn-" + code, color, () =>
                    {
                        var st = NutritionState.Of(capi?.World?.Player?.Entity);
                        return NutrientHover(title, color, code, GutLine(st, def, st?.GetReserve(code) ?? 0f));
                    });
                }
                else
                {
                    string title = Lang.Get(n.LangKey);
                    Row(title, "cn-" + code, color, () =>
                    {
                        var st = NutritionState.Of(capi?.World?.Player?.Entity);
                        float r = st?.GetReserve(code) ?? 0f;
                        string line = StatsHover.Vtml("complexnutrition:reserve-hover", Amount(def, r), StatsHover.Text(NutrientDisplay.DaysText(r)));
                        if (code == NutrientCodes.Fiber && Digestion.Settings != null && st != null)
                            return NutrientHover(title, color, code, line, FiberBoostLine(Digestion.FiberKcalPerHour(Digestion.Settings, st, core.Nutrients)));
                        return NutrientHover(title, color, code, line);
                    });
                }
            }
            leftColumnBoundsW = leftColumnBoundsW.FixedUnder(labelBounds, -5.0);

            compo.AddStaticText(Lang.Get("Physical"), CairoFont.WhiteSmallText().WithWeight(FontWeight.Bold), leftColumnBoundsW.WithFixedWidth(200.0).WithFixedOffset(0.0, 23.0))
                .Execute(() =>
                {
                    leftColumnBoundsW = leftColumnBoundsW.FlatCopy();
                    leftColumnBoundsW.fixedY += 5.0;
                });
            if (health.HasValue)
            {
                compo.AddStaticText(Lang.Get("Health Points"), CairoFont.WhiteDetailText(), leftColumnBoundsW = leftColumnBoundsW.BelowCopy())
                    .AddDynamicText(health + " / " + maxHealth, CairoFont.WhiteDetailText(), rightColumnBounds = rightColumnBounds.FlatCopy().WithFixedPosition(rightColumnBounds.fixedX, leftColumnBoundsW.fixedY).WithFixedHeight(30.0), "health");
            }
            if (saturation.HasValue)
            {
                compo.AddStaticText(Lang.Get("complexnutrition:stats-fullness"), CairoFont.WhiteDetailText(), leftColumnBoundsW = leftColumnBoundsW.BelowCopy())
                    .AddDynamicText((int)Math.Round(saturation.Value) + " / " + (int)Math.Round(maxSaturation.Value), CairoFont.WhiteDetailText(), rightColumnBounds = rightColumnBounds.FlatCopy().WithFixedPosition(rightColumnBounds.fixedX, leftColumnBoundsW.fixedY), "satiety");
            }
            if (bodyTemp != null)
            {
                compo.AddStaticText(Lang.Get("Body Temperature"), CairoFont.WhiteDetailText(), leftColumnBoundsW = leftColumnBoundsW.BelowCopy())
                    .AddRichtext(BodyTempText(bodyTemp), CairoFont.WhiteDetailText(), rightColumnBounds = rightColumnBounds.FlatCopy().WithFixedPosition(rightColumnBounds.fixedX, leftColumnBoundsW.fixedY), "bodytemp");
            }
            if (wetText.Length > 0)
            {
                compo.AddRichtext(wetText, CairoFont.WhiteDetailText(), leftColumnBoundsW = leftColumnBoundsW.BelowCopy());
            }
            compo.AddStaticText(Lang.Get("Walk speed"), CairoFont.WhiteDetailText(), leftColumnBoundsW = leftColumnBoundsW.BelowCopy())
                .AddDynamicText((int)Math.Round(100f * walkspeed) + "%", CairoFont.WhiteDetailText(), rightColumnBounds = rightColumnBounds.FlatCopy().WithFixedPosition(rightColumnBounds.fixedX, leftColumnBoundsW.fixedY), "walkspeed")
                .AddStaticText(Lang.Get("Healing effectivness"), CairoFont.WhiteDetailText(), leftColumnBoundsW = leftColumnBoundsW.BelowCopy())
                .AddDynamicText((int)Math.Round(100f * healing) + "%", CairoFont.WhiteDetailText(), rightColumnBounds = rightColumnBounds.FlatCopy().WithFixedPosition(rightColumnBounds.fixedX, leftColumnBoundsW.fixedY), "healeffectiveness");
            if (saturation.HasValue)
            {
                compo.AddStaticText(Lang.Get("Hunger rate"), CairoFont.WhiteDetailText(), leftColumnBoundsW = leftColumnBoundsW.BelowCopy())
                    .AddDynamicText((int)Math.Round(100f * hungerrate) + "%", CairoFont.WhiteDetailText(), rightColumnBounds = rightColumnBounds.FlatCopy().WithFixedPosition(rightColumnBounds.fixedX, leftColumnBoundsW.fixedY), "hungerrate");
            }
            compo.AddStaticText(Lang.Get("Ranged Accuracy"), CairoFont.WhiteDetailText(), leftColumnBoundsW = leftColumnBoundsW.BelowCopy())
                .AddDynamicText((int)Math.Round(100f * rangedAcc) + "%", CairoFont.WhiteDetailText(), rightColumnBounds = rightColumnBounds.FlatCopy().WithFixedPosition(rightColumnBounds.fixedX, leftColumnBoundsW.fixedY), "rangedweaponacc")
                .AddStaticText(Lang.Get("Ranged Charge Speed"), CairoFont.WhiteDetailText(), leftColumnBoundsW = leftColumnBoundsW.BelowCopy())
                .AddDynamicText((int)Math.Round(100f * rangedSpeed) + "%", CairoFont.WhiteDetailText(), rightColumnBounds = rightColumnBounds.FlatCopy().WithFixedPosition(rightColumnBounds.fixedX, leftColumnBoundsW.fixedY), "rangedweaponchargespeed")
                .EndChildElements()
                .Compose();

            UpdateBars(compo, true);
            return false;
        }

        private static string BodyTempText(ITreeAttribute tempTree)
        {
            float t = tempTree.GetFloat("bodytemp");
            if (t > 37f) t = 37f + (t - 37f) / 10f;
            return $"{t:0.#}°C";
        }

        private static void GetHealthSat(out float? health, out float? maxHealth, out float? saturation, out float? maxSaturation)
        {
            health = maxHealth = saturation = maxSaturation = null;
            var healthTree = capi.World.Player.Entity.WatchedAttributes.GetTreeAttribute("health");
            if (healthTree != null)
            {
                health = healthTree.TryGetFloat("currenthealth");
                maxHealth = healthTree.TryGetFloat("maxhealth");
            }
            if (health.HasValue) health = (float)Math.Round(health.Value, 1);
            if (maxHealth.HasValue) maxHealth = (float)Math.Round(maxHealth.Value, 1);
            var hungerTree = capi.World.Player.Entity.WatchedAttributes.GetTreeAttribute("hunger");
            if (hungerTree != null)
            {
                saturation = hungerTree.TryGetFloat("currentsaturation");
                maxSaturation = hungerTree.TryGetFloat("maxsaturation");
            }
        }
    }
}
