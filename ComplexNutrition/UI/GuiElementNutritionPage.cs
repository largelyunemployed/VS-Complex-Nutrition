using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

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

        /// <summary>The page's height in GUI units (unscaled), for the scrollbar.</summary>
        public double ContentHeight { get; private set; }

        /// <summary>How far the page is scrolled, in GUI units.</summary>
        public double ScrollOffset { get; set; }

        public GuiElementNutritionPage(ICoreClientAPI capi, ElementBounds bounds, double width) : base(capi, bounds)
        {
            this.width = width;
            texture = new LoadedTexture(capi);
        }

        /// <summary>Shows these blocks, redrawing only if they differ from what is shown. True when redrawn.</summary>
        public bool SetBlocks(IList<PageBlock> blocks)
        {
            string next = PageRenderer.KeyOf(blocks);
            if (next == key && texture.TextureId != 0) return false;
            key = next;
            using ImageSurface surface = PageRenderer.Render(api, blocks, width);
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
            api.Render.Render2DTexturePremultipliedAlpha(texture.TextureId,
                Bounds.renderX, Bounds.renderY - scaled(ScrollOffset), texture.Width, texture.Height);
        }

        public override void Dispose()
        {
            base.Dispose();
            texture?.Dispose();
        }
    }
}
