using System;
using Cairo;
using ComplexNutrition.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace ComplexNutrition.UI
{
    /// <summary>
    /// v0.3.3: a small bowl right of the hotbar. Dim while nothing is running low, amber when something is (the same rules as
    /// the window's Running Low list), red when something is nearly gone. Clicking it opens the nutrition window. Each player
    /// can switch it off in the settings menu (saved on their computer).
    /// </summary>
    public sealed class RunningLowIcon : IRenderer
    {
        public const double Size = 30;
        /// <summary>The vanilla hotbar is 850 wide, centred at the bottom, and its top sits 75 above the screen's bottom edge.</summary>
        private const double HotbarHalfWidth = 425, HotbarTop = 75, Gap = 10;

        public static readonly double[] FineColor = { 0.58, 0.64, 0.50, 0.75 };
        public static readonly double[] LowColor = { 0.91, 0.64, 0.24, 1.0 };
        public static readonly double[] EmptyColor = { 0.93, 0.42, 0.37, 1.0 };

        private readonly ICoreClientAPI capi;
        private readonly ComplexNutritionModSystem core;
        private readonly LoadedTexture[] textures = new LoadedTexture[3];
        private float scaleBuilt = -1;
        private int level = -1;   // -1 hidden, 0 fine, 1 low, 2 nearly gone
        private float checkTimer;

        public double RenderOrder => 1.0;
        public int RenderRange => 0;

        public RunningLowIcon(ICoreClientAPI capi, ComplexNutritionModSystem core)
        {
            this.capi = capi;
            this.core = core;
            for (int i = 0; i < textures.Length; i++) textures[i] = new LoadedTexture(capi);
        }

        public void Register()
        {
            capi.Event.RegisterRenderer(this, EnumRenderStage.Ortho, "complexnutrition-lowicon");
            capi.Event.MouseDown += OnMouseDown;
        }

        public void Unregister()
        {
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Ortho);
            capi.Event.MouseDown -= OnMouseDown;
        }

        /// <summary>0 fine, 1 something is running low, 2 something is nearly gone (or the calorie reserve is under its line).</summary>
        public static int LevelOf(NutritionPageData d)
        {
            int result = 0;
            foreach (var n in NutritionPages.RunningLow(d))
            {
                float days = d.State.GetReserve(n.Code) / 100f;
                if (n.IsCalories || days < 0.5f) return 2;
                result = 1;
            }
            return result;
        }

        private (double x, double y, double size) Place()
        {
            double s = GuiElement.scaled(Size);
            double x = capi.Render.FrameWidth / 2.0 + GuiElement.scaled(HotbarHalfWidth + Gap);
            double y = capi.Render.FrameHeight - GuiElement.scaled(HotbarTop) + (GuiElement.scaled(HotbarTop) - s) / 2.0;
            return (x, y, s);
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            checkTimer -= deltaTime;
            if (checkTimer <= 0f)
            {
                checkTimer = 1f;
                level = -1;
                if (core.Client?.HudIcon == true && capi.World?.Player != null)
                {
                    var d = core.BuildPageData();
                    if (d.State != null) level = LevelOf(d);
                }
            }
            if (level < 0) return;
            if (scaleBuilt != RuntimeEnv.GUIScale) Build();
            var (x, y, s) = Place();
            var tex = textures[level];
            if (tex.TextureId != 0) capi.Render.Render2DTexturePremultipliedAlpha(tex.TextureId, (float)x, (float)y, tex.Width, tex.Height, 50f);
        }

        private void OnMouseDown(MouseEvent e)
        {
            if (level < 0 || e.Handled || capi.World?.Player == null) return;
            var (x, y, s) = Place();
            if (e.X < x || e.X > x + s || e.Y < y || e.Y > y + s) return;
            core.ToggleWindow();
            e.Handled = true;
        }

        private void Build()
        {
            scaleBuilt = RuntimeEnv.GUIScale;
            var colors = new[] { FineColor, LowColor, EmptyColor };
            for (int i = 0; i < textures.Length; i++)
            {
                using var surface = Draw(colors[i], GuiElement.scaled(Size));
                capi.Gui.LoadOrUpdateCairoTexture(surface, true, ref textures[i]);
            }
        }

        /// <summary>The icon: a dark rounded tile with a bowl in <paramref name="c"/>. Pure Cairo, so previews can draw it.</summary>
        public static ImageSurface Draw(double[] c, double size)
        {
            int px = Math.Max(1, (int)Math.Ceiling(size));
            var surface = new ImageSurface(Format.Argb32, px, px);
            using var ctx = new Context(surface);
            GuiElement.RoundRectangle(ctx, 0.5, 0.5, px - 1, px - 1, px * 0.18);
            ctx.SetSourceRGBA(0.08, 0.07, 0.06, 0.65);
            ctx.FillPreserve();
            ctx.SetSourceRGBA(c[0] * 0.6, c[1] * 0.6, c[2] * 0.6, c[3]);
            ctx.LineWidth = Math.Max(1, px * 0.05);
            ctx.Stroke();

            // A bowl: the lower half of a circle with a rim, and a little food heaped in it.
            double cx = px / 2.0, cy = px * 0.48, r = px * 0.32;
            ctx.NewPath();
            ctx.Arc(cx, cy, r, 0, Math.PI);
            ctx.ClosePath();
            ctx.SetSourceRGBA(c[0], c[1], c[2], c[3]);
            ctx.Fill();
            ctx.NewPath();
            ctx.Arc(cx, cy, r * 0.62, Math.PI, 2 * Math.PI);
            ctx.ClosePath();
            ctx.SetSourceRGBA(c[0] * 0.8, c[1] * 0.8, c[2] * 0.8, c[3]);
            ctx.Fill();
            ctx.SetSourceRGBA(c[0], c[1], c[2], c[3]);
            ctx.Rectangle(cx - r * 1.12, cy - px * 0.025, r * 2.24, px * 0.06);
            ctx.Fill();
            ctx.Rectangle(cx - r * 0.45, cy + r * 0.95, r * 0.9, px * 0.06);
            ctx.Fill();
            return surface;
        }

        public void Dispose()
        {
            foreach (var t in textures) t?.Dispose();
        }
    }
}
