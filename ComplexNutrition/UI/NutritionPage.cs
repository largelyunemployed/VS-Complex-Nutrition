using System;
using System.Collections.Generic;
using System.Text;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ComplexNutrition.UI
{
    /// <summary>One piece of a nutrition window page: a heading, a bar row, a paragraph or a gap.</summary>
    public abstract class PageBlock
    {
        /// <summary>A text form of the block, so a page is only redrawn when something on it changed.</summary>
        public abstract string Key { get; }
    }

    public sealed class HeadingBlock : PageBlock
    {
        public string Text;
        public HeadingBlock(string text) { Text = text; }
        public override string Key => "H|" + Text;
    }

    /// <summary>A row: label on the left, a bar in the middle, a value (VTML) on the right.</summary>
    public sealed class BarBlock : PageBlock
    {
        public string Label;
        public float Value;
        public float Max;
        /// <summary>A thin line every this much along the bar (one day, one day's need); 0 for none.</summary>
        public float LineInterval;
        public double[] Color;
        /// <summary>VTML shown right of the bar.</summary>
        public string Right;

        // Rounded so a bar that moved by less than a pixel doesn't count as a change.
        public override string Key => "B|" + Label + "|" + Math.Round(Max <= 0 ? 0 : Value / Max * 400) + "|" + Max + "|" + LineInterval + "|" + Right;
    }

    /// <summary>A paragraph of VTML, wrapped to the page width.</summary>
    public sealed class TextBlock : PageBlock
    {
        public string Vtml;
        /// <summary>Drawn in the smaller detail font.</summary>
        public bool Small;
        public TextBlock(string vtml, bool small = false) { Vtml = vtml; Small = small; }
        public override string Key => "T|" + (Small ? "s|" : "|") + Vtml;
    }

    public sealed class GapBlock : PageBlock
    {
        public double Height;
        public GapBlock(double height) { Height = height; }
        public override string Key => "G|" + Height;
    }

    /// <summary>
    /// Draws a page of blocks into a Cairo surface, at the GUI scale: bars drawn the way the game draws its stat bars,
    /// text through the game's own rich text element, so colours and fonts match the tooltips and the Stats panel.
    /// It touches nothing but Cairo and the rich text element, so it also runs outside the game for previews.
    /// </summary>
    public static class PageRenderer
    {
        public const double LabelWidth = 150;
        public const double BarX = 155;
        public const double BarWidth = 160;
        public const double BarHeight = 10;
        public const double RightX = 325;
        public const double RowHeight = 22;

        public static string KeyOf(IList<PageBlock> blocks)
        {
            var sb = new StringBuilder();
            foreach (var b in blocks) sb.Append(b.Key).Append('\n');
            return sb.ToString();
        }

        private sealed class Placed
        {
            public PageBlock Block;
            public double Y;
            public double Height;
            public GuiElementRichtext Text;      // heading or paragraph
            public GuiElementRichtext Label;     // bar label
            public GuiElementRichtext Value;     // bar value
        }

        private static GuiElementRichtext Rich(ICoreClientAPI capi, string vtml, CairoFont font, double width)
        {
            var bounds = ElementBounds.Fixed(0.0, 0.0, width, 10.0).WithParent(ElementBounds.Empty);
            var text = new GuiElementRichtext(capi, VtmlUtil.Richtextify(capi, vtml ?? "", font), bounds);
            text.CalcHeightAndPositions();
            return text;
        }

        /// <summary>Draws the page <paramref name="width"/> GUI units wide. The caller disposes the surface.</summary>
        public static ImageSurface Render(ICoreClientAPI capi, IList<PageBlock> blocks, double width)
        {
            var placed = new List<Placed>();
            double y = 0;
            var detail = CairoFont.WhiteDetailText();
            var body = CairoFont.WhiteSmallText().WithFontSize(15);
            var heading = CairoFont.WhiteSmallText().WithWeight(FontWeight.Bold).WithColor(GuiStyle.ActiveButtonTextColor);
            bool first = true;

            // Everything below is in scaled (pixel) units.
            foreach (var block in blocks)
            {
                var p = new Placed { Block = block };
                switch (block)
                {
                    case HeadingBlock h:
                        if (!first) y += GuiElement.scaled(12);
                        p.Text = Rich(capi, "<font color=\"#d8c48c\" weight=\"bold\">" + Esc(h.Text) + "</font>", heading, width);
                        p.Height = p.Text.TotalHeight + GuiElement.scaled(4);
                        break;
                    case BarBlock b:
                        p.Label = Rich(capi, b.Label, detail, LabelWidth);
                        p.Value = Rich(capi, b.Right, detail, width - RightX);
                        p.Height = Math.Max(GuiElement.scaled(RowHeight), Math.Max(p.Label.TotalHeight, p.Value.TotalHeight) + GuiElement.scaled(4));
                        break;
                    case TextBlock t:
                        p.Text = Rich(capi, t.Vtml, t.Small ? detail : body, width);
                        p.Height = p.Text.TotalHeight + GuiElement.scaled(3);
                        break;
                    case GapBlock g:
                        p.Height = GuiElement.scaled(g.Height);
                        break;
                }
                p.Y = y;
                y += p.Height;
                placed.Add(p);
                first = false;
            }

            int w = (int)Math.Ceiling(GuiElement.scaled(width));
            int hgt = (int)Math.Max(1, Math.Ceiling(y + GuiElement.scaled(6)));
            var surface = new ImageSurface(Format.Argb32, Math.Max(1, w), hgt);
            using (var ctx = new Context(surface))
            {
                foreach (var p in placed)
                {
                    if (p.Text != null) Draw(ctx, surface, p.Text, 0, p.Y);
                    if (p.Block is BarBlock b)
                    {
                        double rowMid = p.Y + GuiElement.scaled(RowHeight) / 2.0;
                        Draw(ctx, surface, p.Label, 0, rowMid - p.Label.TotalHeight / 2.0);
                        DrawBar(ctx, GuiElement.scaled(BarX), rowMid - GuiElement.scaled(BarHeight) / 2.0, GuiElement.scaled(BarWidth), GuiElement.scaled(BarHeight), b);
                        Draw(ctx, surface, p.Value, GuiElement.scaled(RightX), rowMid - Math.Min(p.Value.TotalHeight, GuiElement.scaled(RowHeight)) / 2.0);
                    }
                }
            }
            foreach (var p in placed)
            {
                p.Text?.Dispose();
                p.Label?.Dispose();
                p.Value?.Dispose();
            }
            return surface;
        }

        private static void Draw(Context ctx, ImageSurface surface, GuiElementRichtext text, double x, double y)
        {
            ctx.Save();
            ctx.Translate(x, y);
            text.ComposeFor(text.Bounds.CopyOnlySize(), ctx, surface);
            ctx.Restore();
        }

        /// <summary>A bar as the game draws its stat bars: a dark trough, the filled part in the bar's colour, a line per interval.</summary>
        private static void DrawBar(Context ctx, double x, double y, double w, double h, BarBlock b)
        {
            GuiElement.RoundRectangle(ctx, x, y, w, h, 1.0);
            ctx.SetSourceRGBA(0.15, 0.15, 0.15, 1.0);
            ctx.Fill();

            double share = b.Max > 0 ? Math.Clamp(b.Value / b.Max, 0.0, 1.0) : 0.0;
            var c = b.Color ?? GuiStyle.FoodBarColor;
            if (share > 0.005)
            {
                double fw = Math.Max(2.0, w * share);
                GuiElement.RoundRectangle(ctx, x, y, fw, h, 1.0);
                ctx.SetSourceRGB(c[0], c[1], c[2]);
                ctx.FillPreserve();
                ctx.SetSourceRGB(c[0] * 0.4, c[1] * 0.4, c[2] * 0.4);
                ctx.LineWidth = GuiElement.scaled(1.5);
                ctx.Stroke();
                // A soft highlight along the top, like the game's embossed bars.
                ctx.SetSourceRGBA(1, 1, 1, 0.18);
                ctx.Rectangle(x + 1, y + 1, Math.Max(0, fw - 2), Math.Max(1, h * 0.3));
                ctx.Fill();
            }

            if (b.LineInterval > 0 && b.Max > 0 && b.Max / b.LineInterval < 60)
            {
                ctx.SetSourceRGBA(0, 0, 0, 0.45);
                ctx.LineWidth = 1.0;
                for (double v = b.LineInterval; v < b.Max - 0.001; v += b.LineInterval)
                {
                    double lx = Math.Round(x + w * v / b.Max) + 0.5;
                    ctx.MoveTo(lx, y + 1);
                    ctx.LineTo(lx, y + h - 1);
                }
                ctx.Stroke();
            }

            // The trough's edge, drawn last so it frames the fill.
            GuiElement.RoundRectangle(ctx, x, y, w, h, 1.0);
            ctx.SetSourceRGBA(0, 0, 0, 0.6);
            ctx.LineWidth = 1.0;
            ctx.Stroke();
        }

        private static string Esc(string text) => string.IsNullOrEmpty(text) ? "" : text.Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
