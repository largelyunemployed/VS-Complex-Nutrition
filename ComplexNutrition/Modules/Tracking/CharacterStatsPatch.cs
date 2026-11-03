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
    /// bar per nutrient reserve in place of vanilla's five food category bars. Everything else on the panel
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
            }
            if (!listening)
            {
                listening = true;
                capi.Event.LevelFinalize += ListenForChanges;
                hudListener = capi.Event.RegisterGameTickListener(_ => LabelFullnessBar(), 1000);
            }
        }

        private static long hudListener;
        private static GuiElementStatbar labelledBar;

        /// <summary>
        /// Vanilla's satiety bar next to the hotbar only shows "1070 / 1500" on hover. Label it as fullness, so
        /// it reads as how much the stomach holds, next to the calorie reserve. The HUD recomposes now and then
        /// (resizing, GUI scale), which makes a new bar, so this checks once a second.
        /// </summary>
        private static void LabelFullnessBar()
        {
            if (capi == null) return;
            var hud = capi.Gui.LoadedGuis.FirstOrDefault(g => g.GetType().Name == "HudStatbar");
            var bar = hud?.Composers["statbar"]?.GetStatbar("saturationstatbar");
            if (bar == null || bar == labelledBar) return;
            labelledBar = bar;
            bar.onGetStatbarValue = () =>
            {
                var hunger = capi?.World?.Player?.Entity?.WatchedAttributes.GetTreeAttribute("hunger");
                if (hunger == null) return "";
                return Lang.Get("complexnutrition:fullness-hover",
                    (int)Math.Round(hunger.GetFloat("currentsaturation")), (int)Math.Round(hunger.GetFloat("maxsaturation")));
            };
            // The hover text is only redrawn when the bar's value is set, so set it once now.
            var tree = capi.World?.Player?.Entity?.WatchedAttributes.GetTreeAttribute("hunger");
            if (tree != null) bar.SetValues(tree.GetFloat("currentsaturation"), 0, tree.GetFloat("maxsaturation"));
        }

        public static void Remove()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            if (capi != null && listening)
            {
                capi.Event.LevelFinalize -= ListenForChanges;
                capi.Event.UnregisterGameTickListener(hudListener);
            }
            labelledBar = null;
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
                bar.SetLineInterval(100);   // one block per day of storage
                bar.SetValues(reserve, 0, max);
            }
        }

        /// <summary>Vanilla's ComposeStatsGui with the nutrition block swapped for our reserve bars.</summary>
        public static bool ComposeStatsPrefix(CharacterExtraDialogs __instance)
        {
            var state = TrackedState();
            if (state == null) return true;
            var dlg = DlgRef(__instance);
            if (dlg == null) return true;
            var Composers = dlg.Composers;
            var nutrients = core.Nutrients.All;

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
            for (int i = 0; i < nutrients.Count; i++)
            {
                var n = nutrients[i];
                if (i > 0) labelBounds = labelBounds.BelowCopy();
                var barBounds = ElementBounds.Fixed(120.0, labelBounds.fixedY + (RowHeight - 8.0) / 2.0 + 1.0, 112.0, 8.0);
                var color = GroupColors.TryGetValue(n.Group ?? "", out var c) ? c : GuiStyle.FoodBarColor;
                compo.AddStaticText(Lang.Get(n.LangKey), CairoFont.WhiteDetailText().WithFontSize(13), labelBounds)
                     .AddStatbar(barBounds, color, "cn-" + n.Code);
                var code = n.Code;
                compo.GetStatbar("cn-" + code).onGetStatbarValue = () =>
                {
                    float r = NutritionState.Of(capi?.World?.Player?.Entity)?.GetReserve(code) ?? 0f;
                    return Lang.Get("complexnutrition:reserve-hover", Math.Round(r), TrackingModule.DaysText(r))
                        + "\n" + Lang.Get("complexnutrition:nutrient-" + code + "-role")
                        + "\n" + Lang.Get("complexnutrition:effect-placeholder");
                };
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
                compo.AddStaticText(Lang.Get("Satiety"), CairoFont.WhiteDetailText(), leftColumnBoundsW = leftColumnBoundsW.BelowCopy())
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
