using System;
using System.Globalization;
using System.Text;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using ComplexNutrition.Core;

namespace ComplexNutrition.Modules.Tracking
{
    /// <summary>
    /// Client: the hover box for our Stats panel and HUD bars. Vanilla's bar hover draws plain text in one font and
    /// colour, so our bars turn it off and this draws a rich text box instead (VTML, the same as item tooltips), with
    /// a coloured title and bold section headings. The texture is only redrawn when the text changes.
    /// </summary>
    internal sealed class StatsHover : IRenderer, IDisposable
    {
        /// <summary>Widest the box's text may be, in GUI units (scaled with the GUI scale setting).</summary>
        public const double MaxWidth = 550.0;
        private const double Padding = 6.0;

        /// <summary>Section heading colour: a muted gold that reads on the dark box.</summary>
        public const string HeadingColor = "#d8c48c";
        /// <summary>Colour for notes about features that do nothing yet.</summary>
        public const string NoteColor = "#9a9a9a";
        /// <summary>A line break with a small invisible line after it: a gap of about half a line between sections.</summary>
        private const string Gap = "<font size=\"8\" opacity=\"0\"><br>.<br></font>";

        private readonly ICoreClientAPI capi;
        private LoadedTexture texture;
        private string shown;
        private bool visible;

        /// <summary>Called each frame before drawing, to pick what to show (or hide).</summary>
        private readonly Action beforeRender;

        public StatsHover(ICoreClientAPI capi, Action beforeRender)
        {
            this.capi = capi;
            this.beforeRender = beforeRender;
            texture = new LoadedTexture(capi);
        }

        // After the GUI (1.0), so the box sits over every dialog.
        public double RenderOrder => 1.01;
        public int RenderRange => 0;

        /// <summary>Shows the box with this VTML text, redrawing it only if the text changed.</summary>
        public void Show(string vtml)
        {
            if (string.IsNullOrEmpty(vtml)) { visible = false; return; }
            visible = true;
            if (vtml == shown && texture.TextureId != 0) return;
            shown = vtml;
            Compose(vtml);
        }

        public void Hide() => visible = false;

        private void Compose(string vtml)
        {
            var font = CairoFont.WhiteSmallText();
            var bounds = ElementBounds.Fixed(0.0, 0.0, MaxWidth, 10.0).WithParent(ElementBounds.Empty);
            var text = new GuiElementRichtext(capi, VtmlUtil.Richtextify(capi, vtml, font), bounds);
            try
            {
                text.CalcHeightAndPositions();
                double pad = GuiElement.scaled(Padding);
                int width = (int)Math.Ceiling(Math.Min(text.MaxLineWidth, GuiElement.scaled(MaxWidth)) + 2 * pad);
                int height = (int)Math.Ceiling(text.TotalHeight + 2 * pad);
                using var surface = new ImageSurface(Format.Argb32, Math.Max(1, width), Math.Max(1, height));
                using var ctx = new Context(surface);

                // The box, as vanilla draws its hover text background.
                GuiElement.RoundRectangle(ctx, 0.0, 0.0, width, height, 1.0);
                ctx.SetSourceRGBA(GuiStyle.DialogStrongBgColor);
                ctx.FillPreserve();
                ctx.SetSourceRGBA(GuiStyle.DialogBorderColor);
                ctx.LineWidth = 2.0;
                ctx.Stroke();

                ctx.Save();
                ctx.Translate(pad, pad);
                text.ComposeFor(bounds.CopyOnlySize(), ctx, surface);
                ctx.Restore();
                capi.Gui.LoadOrUpdateCairoTexture(surface, false, ref texture);
            }
            finally
            {
                text.Dispose();
            }
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            beforeRender?.Invoke();
            if (!visible || texture.TextureId == 0) return;
            int w = texture.Width, h = texture.Height;
            int screenW = capi.Render.FrameWidth, screenH = capi.Render.FrameHeight;
            double x = capi.Input.MouseX + GuiElement.scaled(16.0);
            double y = capi.Input.MouseY + GuiElement.scaled(16.0);
            // Keep the box on screen: left of the mouse if it would run off the right, above it if off the bottom.
            if (x + w > screenW) x = Math.Max(0, capi.Input.MouseX - GuiElement.scaled(8.0) - w);
            if (y + h > screenH) y = Math.Max(0, screenH - h);
            capi.Render.Render2DTexturePremultipliedAlpha(texture.TextureId, x, y, w, h, 10000f);
        }

        public void Dispose()
        {
            texture.Dispose();
        }

        // ---- VTML helpers ----

        /// <summary>Text with VTML's tag characters escaped.</summary>
        public static string Esc(string text) =>
            string.IsNullOrEmpty(text) ? "" : text.Replace("<", "&lt;").Replace(">", "&gt;");

        /// <summary>Plain text as VTML, with every amount in its unit's colour (<see cref="UnitColors"/>).</summary>
        public static string Text(string text) => UnitColors.Paint(Esc(text));

        /// <summary>Plain text in a colour, as VTML; amounts inside keep their unit colours.</summary>
        public static string Paint(string text, string color) =>
            "<font color=\"" + color + "\">" + Text(text) + "</font>";

        /// <summary>A lang entry as VTML: its own text escaped, with the (already VTML) arguments put in.</summary>
        public static string Vtml(string key, params object[] vtmlArgs) =>
            string.Format(CultureInfo.InvariantCulture, Esc(Lang.GetUnformatted(key)), vtmlArgs);

        /// <summary>A bar colour as #rrggbb, lightened so it reads as text on the dark box.</summary>
        public static string Hex(double[] rgba, double lighten = 0.35)
        {
            static int C(double v, double l) => (int)Math.Round(Math.Clamp(v + (1.0 - v) * l, 0.0, 1.0) * 255.0);
            return "#" + C(rgba[0], lighten).ToString("x2", CultureInfo.InvariantCulture)
                + C(rgba[1], lighten).ToString("x2", CultureInfo.InvariantCulture)
                + C(rgba[2], lighten).ToString("x2", CultureInfo.InvariantCulture);
        }

        /// <summary>Builds a hover: a bold coloured title, then sections each led by a bold heading.</summary>
        public sealed class Builder
        {
            private readonly StringBuilder sb = new();

            public Builder(string title, string color)
            {
                sb.Append("<font color=\"").Append(color).Append("\" weight=\"bold\" scale=\"125%\">").Append(Esc(title)).Append("</font>");
            }

            /// <summary>A section: its heading on its own line, then each non-empty line below it. Lines are VTML
            /// (use <see cref="Text"/> for plain text).</summary>
            public Builder Section(string heading, params string[] lines)
            {
                bool any = false;
                foreach (var line in lines) any |= !string.IsNullOrEmpty(line);
                if (!any) return this;
                sb.Append(Gap).Append("<font color=\"").Append(HeadingColor).Append("\" weight=\"bold\">").Append(Esc(heading)).Append("</font>");
                foreach (var line in lines)
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    foreach (var part in line.Split('\n')) sb.Append("<br>").Append(part);
                }
                return this;
            }

            /// <summary>A line in the muted note colour, without a heading.</summary>
            public Builder Note(string line)
            {
                if (!string.IsNullOrEmpty(line)) sb.Append(Gap).Append("<font color=\"").Append(NoteColor).Append("\">").Append(Esc(line)).Append("</font>");
                return this;
            }

            public override string ToString() => sb.ToString();
        }
    }
}
