using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ComplexNutrition.UI
{
    /// <summary>
    /// A nutrition window page: drawn once into a texture by <see cref="PageRenderer"/> and redrawn only when its
    /// blocks change. Sits inside a clip so it can scroll.
    /// </summary>
    public sealed class GuiElementNutritionPage : GuiElement
    {
        private LoadedTexture texture;
        private string key;
        private readonly double width;
        private List<Hotspot> hotspots = new();
        private Hotspot hovered;
        private LoadedTexture tipTexture;
        private string tipKey;

        /// <summary>How wide a hover tooltip may grow, in GUI units.</summary>
        public const double TipWidth = 260;

        /// <summary>The page's height in GUI units (unscaled), for the scrollbar.</summary>
        public double ContentHeight { get; private set; }

        /// <summary>How far the page is scrolled, in GUI units.</summary>
        public double ScrollOffset { get; set; }

        public GuiElementNutritionPage(ICoreClientAPI capi, ElementBounds bounds, double width) : base(capi, bounds)
        {
            this.width = width;
            texture = new LoadedTexture(capi);
            tipTexture = new LoadedTexture(capi);
        }

        /// <summary>Shows these blocks, redrawing only if they differ from what is shown. True when redrawn.</summary>
        public bool SetBlocks(IList<PageBlock> blocks)
        {
            string next = PageRenderer.KeyOf(blocks);
            if (next == key && texture.TextureId != 0) return false;
            key = next;
            var spots = new List<Hotspot>();
            using ImageSurface surface = PageRenderer.Render(api, blocks, width, spots);
            hotspots = spots;
            hovered = null;
            api.Gui.LoadOrUpdateCairoTexture(surface, true, ref texture);
            ContentHeight = surface.Height / RuntimeEnv.GUIScale;
            return true;
        }

        public override void ComposeElements(Context ctx, ImageSurface surface)
        {
            Bounds.CalcWorldBounds();
        }

        public override void RenderInteractiveElements(float deltaTime)
        {
            if (texture.TextureId == 0) return;
            double top = Bounds.renderY - scaled(ScrollOffset);
            api.Render.Render2DTexturePremultipliedAlpha(texture.TextureId, Bounds.renderX, top, texture.Width, texture.Height);

            // What the mouse is over, if it is over the visible part of the page.
            hovered = null;
            int mx = api.Input.MouseX, my = api.Input.MouseY;
            var clip = Bounds.ParentBounds;
            if (clip == null || clip.PointInside(mx, my))
            {
                double px = mx - Bounds.renderX, py = my - top;
                for (int i = hotspots.Count - 1; i >= 0; i--)
                {
                    if (!hotspots[i].Contains(px, py)) continue;
                    hovered = hotspots[i];
                    break;
                }
            }
            if (hovered != null && hovered.Highlight)
            {
                api.Render.RenderRectangle((float)(Bounds.renderX + hovered.X), (float)(top + hovered.Y), 50f, (float)hovered.W, (float)hovered.H, ColorUtil.ToRgba(28, 255, 255, 255));
            }
        }

        /// <summary>Draws the hovered spot's tooltip beside the mouse, above everything in the window. Call after the window has drawn.</summary>
        public void RenderTooltip()
        {
            if (hovered == null || string.IsNullOrEmpty(hovered.Vtml)) return;
            if (hovered.Vtml != tipKey || tipTexture.TextureId == 0)
            {
                tipKey = hovered.Vtml;
                using ImageSurface surface = PageRenderer.RenderTip(api, hovered.Vtml, TipWidth);
                api.Gui.LoadOrUpdateCairoTexture(surface, true, ref tipTexture);
            }
            double x = api.Input.MouseX + scaled(14), y = api.Input.MouseY + scaled(14);
            if (x + tipTexture.Width > api.Render.FrameWidth) x = api.Input.MouseX - tipTexture.Width - scaled(6);
            if (y + tipTexture.Height > api.Render.FrameHeight) y = api.Render.FrameHeight - tipTexture.Height;
            api.Render.Render2DTexturePremultipliedAlpha(tipTexture.TextureId, x, y, tipTexture.Width, tipTexture.Height, 200f);
        }

        public override void Dispose()
        {
            base.Dispose();
            texture?.Dispose();
            tipTexture?.Dispose();
        }
    }
}
