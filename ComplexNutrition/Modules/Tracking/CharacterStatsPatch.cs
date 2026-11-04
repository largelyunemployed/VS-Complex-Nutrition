using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using ComplexNutrition.Core;
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
    /// bar above the hotbar shows the calorie tank in place of satiety. Everything else on the panel
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

        /// <summary>Bar colours per nutrient group, in the game's muted palette.</summary>
        private static readonly Dictionary<string, double[]> GroupColors = new()
        {
            ["Energy"] = new[] { 0.80, 0.55, 0.22, 1.0 },
            ["Macro"] = GuiStyle.FoodBarColor,
            ["FatSolubleVitamin"] = new[] { 0.78, 0.68, 0.30, 1.0 },
            ["WaterSolubleVitamin"] = new[] { 0.86, 0.46, 0.24, 1.0 },
            ["Mineral"] = new[] { 0.45, 0.56, 0.66, 1.0 },
        };

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
                hoverListener = capi.Event.RegisterGameTickListener(OnHoverTick, 100);
            }
        }

        private static Type hudType;
        private static System.Reflection.FieldInfo hudBarField;
        private static System.Reflection.FieldInfo hudLastField;
        private static GuiElementStatbar hudBar;
        private static float hudKcal = -1f, hudFullness = -1f, hudMax = -1f;
        private static bool hudOurs;

        /// <summary>The calorie tank in kcal and its size, or false while the server does not track this player.</summary>
        private static bool CalorieTank(out float kcal, out float maxKcal, out float fullness, out float maxFullness)
        {
            kcal = maxKcal = fullness = maxFullness = 0f;
            var attrs = capi?.World?.Player?.Entity?.WatchedAttributes;
            var tree = attrs?.GetTreeAttribute(NutritionState.TreeKey);
            var def = core?.Nutrients.Get(NutrientCodes.Calories);
            if (tree == null || def == null || !tree.GetBool("tracking")) return false;
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
        /// calorie tank instead, one block per active day, with fullness in its hover text.
        /// </summary>
        public static bool HudUpdatePrefix(object __instance)
        {
            var bar = hudBarField?.GetValue(__instance) as GuiElementStatbar;
            if (!CalorieTank(out var kcal, out var maxKcal, out var fullness, out var maxFullness))
            {
                if (hudOurs && bar != null)
                {
                    // Tracking stopped: hand the bar back to vanilla and make it redraw.
                    bar.onGetStatbarValue = null;
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
                bar.onGetStatbarValue = () => MouseOver(bar) ? WrapHover(HudHover()) : "";
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
            if (!CalorieTank(out var kcal, out var maxKcal, out var fullness, out var maxFullness)) return "";
            var def = core.Nutrients.Get(NutrientCodes.Calories);
            float pct = def.ToPercent(kcal);
            return Lang.Get("complexnutrition:hud-calories", def.Format(pct, core.Nutrients.ShowPercent), TrackingModule.DaysText(pct))
                + "\n" + Lang.Get("complexnutrition:fullness-hover", (int)Math.Round(fullness), (int)Math.Round(maxFullness));
        }

        /// <summary>Vanilla flashes the bar below 20% satiety; while tracking runs it flashes below 10% of a full calorie tank instead.</summary>
        public static void HudFlashPostfix(object __instance)
        {
            if (!CalorieTank(out var kcal, out var maxKcal, out _, out _)) return;
            if (hudBarField?.GetValue(__instance) is not GuiElementStatbar bar) return;
            var mode = capi.World.Player.WorldData.CurrentGameMode;
            float below = Digestion.Settings?.FlashBelow ?? 0.1f;
            bar.ShouldFlash = mode != EnumGameMode.Creative && mode != EnumGameMode.Spectator && maxKcal > 0f && kcal / maxKcal < below;
        }

        public static void Remove()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            if (capi != null && listening)
            {
                capi.Event.LevelFinalize -= ListenForChanges;
                capi.Event.UnregisterGameTickListener(hoverListener);
            }
            statHovers.Clear();
            barValues.Clear();
            hoverBar = null;
            hoverText = null;
            if (hudBar != null) hudBar.onGetStatbarValue = null;
            hudBar = null;
            hudOurs = false;
            listening = false;
            capi = null;
            core = null;
        }

        private static void ListenForChanges()
        {
            capi?.World?.Player?.Entity?.WatchedAttributes.RegisterModifiedListener(NutritionState.TreeKey, () =>
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

        public static bool UpdateStatBarsPrefix(CharacterExtraDialogs __instance)
        {
            if (TrackedState() == null) return true;
            var dlg = DlgRef(__instance);
            var composer = dlg?.Composers["playerstats"];
            if (composer == null || !dlg.IsOpened()) return false;

            var hunger = capi.World.Player.Entity.WatchedAttributes.GetTreeAttribute("hunger");
            if (hunger != null)
            {
                composer.GetDynamicText("satiety")?.SetNewText((int)Math.Round(hunger.GetFloat("currentsaturation")) + " / " + (int)Math.Round(hunger.GetFloat("maxsaturation")));
            }
            UpdateBars(composer);
            return false;
        }

        private static void UpdateBars(GuiComposer composer)
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
                SetBar(bar, reserve, max);
            }
            var protein = composer.GetStatbar(Protein24Bar);
            if (protein != null)
            {
                float need = Digestion.Settings?.ProteinNeedGrams ?? 64f;
                float most = Digestion.Settings?.ProteinMaxGrams ?? 128f;
                float pct = need > 0f ? state.Protein24 / need * 100f : 0f;
                protein.SetLineInterval(100);   // a line at the day's need, the end of the bar at the most the body uses
                SetBar(protein, pct, Math.Max(most / Math.Max(1f, need) * 100f, pct));
            }
        }

        private const string Protein24Bar = "cn-protein24";

        /// <summary>
        /// The hover's last line: a nutrient's own "-effect" text when it has one (empty = none, for nutrients whose
        /// description already says what they do), otherwise the "planned" note.
        /// </summary>
        private static string EffectLine(string code)
        {
            string key = "complexnutrition:nutrient-" + code + "-effect";
            string text = Lang.HasTranslation(key) ? Lang.Get(key) : Lang.Get("complexnutrition:effect-placeholder");
            return string.IsNullOrEmpty(text) ? "" : "\n" + text;
        }

        // Vanilla redraws a bar's hover text into a texture every time the bar's value is set, whether or not the
        // mouse is over it. With fifteen bars and long descriptions that stalled the game each time the reserves
        // changed (every few seconds, and several times per food eaten). So a bar only draws its hover text while the
        // mouse is over it, only bars that visibly moved are redrawn, and OnHoverTick redraws the bar under the mouse
        // when the mouse arrives or its text changes.
        private static readonly Dictionary<GuiElementStatbar, System.Func<string>> statHovers = new();
        private static readonly Dictionary<GuiElementStatbar, float[]> barValues = new();
        private static GuiElementStatbar hoverBar;
        private static string hoverText;
        private static long hoverListener;

        private static bool MouseOver(GuiElementStatbar bar) =>
            capi != null && bar?.Bounds != null && bar.Bounds.PointInside(capi.Input.MouseX, capi.Input.MouseY);

        /// <summary>Sets a Stats bar's value unless it would move by less than a pixel or so.</summary>
        private static void SetBar(GuiElementStatbar bar, float value, float max)
        {
            if (barValues.TryGetValue(bar, out var last) && last[1] == max && Math.Abs(last[0] - value) <= max * 0.002f) return;
            barValues[bar] = new[] { value, max };
            bar.SetValues(value, 0f, max);
        }

        private static void OnHoverTick(float dt)
        {
            if (capi == null) return;
            GuiElementStatbar bar = null;
            System.Func<string> text = null;
            if (hudOurs && hudBar != null && MouseOver(hudBar))
            {
                bar = hudBar;
                text = HudHover;
            }
            else if (statHovers.Count > 0)
            {
                var dialogs = capi.ModLoader.GetModSystem<CharacterExtraDialogs>();
                if (dialogs != null && DlgRef(dialogs)?.IsOpened() == true)
                {
                    foreach (var kv in statHovers)
                    {
                        if (!MouseOver(kv.Key)) continue;
                        bar = kv.Key;
                        text = kv.Value;
                        break;
                    }
                }
            }
            if (bar == null)
            {
                hoverBar = null;
                hoverText = null;
                return;
            }
            string now = text();
            if (bar == hoverBar && now == hoverText) return;
            hoverBar = bar;
            hoverText = now;
            // Setting the same values again makes vanilla redraw the hover text, now that the mouse is over the bar.
            if (bar == hudBar) { if (hudKcal >= 0f && hudMax > 0f) bar.SetValues(hudKcal, 0f, hudMax); }
            else if (barValues.TryGetValue(bar, out var v)) bar.SetValues(v[0], 0f, v[1]);
        }

        /// <summary>Widest a hover box may be, in GUI units (scaled with the GUI scale setting).</summary>
        private const double HoverWidth = 450.0;
        private static CairoFont hoverFont;

        /// <summary>
        /// Vanilla draws a bar's hover text one line per "\n" with no wrapping, so long descriptions run off
        /// screen. This breaks each line between words so none is wider than <see cref="HoverWidth"/>,
        /// measured in the font vanilla draws the hover in (GuiElementStatbar.valueFont).
        /// </summary>
        private static string WrapHover(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            hoverFont ??= CairoFont.WhiteSmallText();
            double max = GuiElement.scaled(HoverWidth);
            var sb = new System.Text.StringBuilder(text.Length + 16);
            var lines = text.Split('\n');
            for (int li = 0; li < lines.Length; li++)
            {
                if (li > 0) sb.Append('\n');
                string line = "";
                foreach (var word in lines[li].Split(' '))
                {
                    string next = line.Length == 0 ? word : line + " " + word;
                    if (line.Length > 0 && hoverFont.GetTextExtents(next).Width > max)
                    {
                        sb.Append(line).Append('\n');
                        line = word;
                    }
                    else line = next;
                }
                sb.Append(line);
            }
            return sb.ToString();
        }

        /// <summary>
        /// A gut pool's first hover line. With the advanced nutrition display: grams, the calories they will give
        /// (after diminishing returns), time to absorb and the current absorption rate; otherwise grams and time.
        /// </summary>
        private static string GutLine(NutritionState st, NutrientDefinition def, float reserve)
        {
            string code = def.Code;
            string hours = TrackingModule.GutHoursText(st?.GetGutHours(code) ?? 0f, reserve);
            var s = Digestion.Settings;
            var pool = s?.Pool(code);
            if (st == null || pool == null || !Digestion.AdvancedDisplay(capi?.World, st) || reserve <= 0.01f)
            {
                return Lang.Get("complexnutrition:gut-hover", def.Format(reserve, core.Nutrients.ShowPercent), hours);
            }
            float grams = reserve / 100f * def.DailyTarget;
            float window = Digestion.WindowGrams(st, code, pool);
            float kcal = pool.Effective(window, grams) * pool.KcalPerGram;
            float rate = pool.Rate(grams, Digestion.FiberSpeed(s, st, pool));
            float kcalRate = pool.Effective(window, rate) * pool.KcalPerGram;
            return Lang.Get("complexnutrition:gut-hover-advanced", Num(grams), Num(kcal), hours, Num(rate), Num(kcalRate));
        }

        private static string Num(float v) => v >= 10f ? Math.Round(v).ToString("#,0", System.Globalization.CultureInfo.InvariantCulture) : v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

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
            hoverBar = null;
            hoverText = null;

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
                statHovers[statbar] = hover;
                statbar.onGetStatbarValue = () => MouseOver(statbar) ? WrapHover(hover()) : "";
            }

            for (int i = 0; i < nutrients.Count; i++)
            {
                var n = nutrients[i];
                var color = GroupColors.TryGetValue(n.Group ?? "", out var c) ? c : GuiStyle.FoodBarColor;
                var code = n.Code;
                var def = n;
                if (code == NutrientCodes.Protein && IsGutPool(code))
                {
                    Row(Lang.Get(n.LangKey), Protein24Bar, color, () =>
                    {
                        var st = NutritionState.Of(capi?.World?.Player?.Entity);
                        float r = st?.GetReserve(code) ?? 0f;
                        return GutLine(st, def, r)
                            + "\n" + Lang.Get("complexnutrition:protein24-hover", Math.Round(st?.Protein24 ?? 0f),
                                Math.Round(Digestion.Settings?.ProteinNeedGrams ?? 64f), Math.Round(Digestion.Settings?.ProteinMaxGrams ?? 128f))
                            + "\n" + Lang.Get("complexnutrition:nutrient-" + code + "-role")
                            + EffectLine(code);
                    });
                }
                else if (IsGutPool(code))
                {
                    Row(Lang.Get("complexnutrition:stats-gut-" + code), "cn-" + code, color, () =>
                    {
                        var st = NutritionState.Of(capi?.World?.Player?.Entity);
                        float r = st?.GetReserve(code) ?? 0f;
                        return GutLine(st, def, r)
                            + "\n" + Lang.Get("complexnutrition:nutrient-" + code + "-role")
                            + EffectLine(code);
                    });
                }
                else
                {
                    Row(Lang.Get(n.LangKey), "cn-" + code, color, () =>
                    {
                        float r = NutritionState.Of(capi?.World?.Player?.Entity)?.GetReserve(code) ?? 0f;
                        return Lang.Get("complexnutrition:reserve-hover", def.Format(r, core.Nutrients.ShowPercent), TrackingModule.DaysText(r))
                            + "\n" + Lang.Get("complexnutrition:nutrient-" + code + "-role")
                            + EffectLine(code);
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

            UpdateBars(compo);
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
