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
        /// <summary>An arrow just left of the bar: 1 rising, 0 steady, -1 falling; null for none.</summary>
        public int? Trend;
        /// <summary>What hovering the trend arrow shows (VTML, since v0.3.6); null for no hover.</summary>
        public string TrendTip;
        /// <summary>What hovering the value right of the bar shows (VTML, since v0.3.6); null for no hover.</summary>
        public string RightTip;
        /// <summary>What hovering the row's label and bar shows (VTML, since v0.3.6i); null for no hover.</summary>
        public string Tip;

        // Rounded so a bar that moved by less than a pixel doesn't count as a change.
        public override string Key => "B|" + Label + "|" + Math.Round(Max <= 0 ? 0 : Value / Max * 400) + "|" + Max + "|" + LineInterval + "|" + Right + "|" + Trend + "|" + TrendTip + "|" + RightTip + "|" + Tip;
    }

    /// <summary>
    /// A small table (since v0.3.6i): columns start at <see cref="Columns"/> (GUI units from the left), the first row drawn as
    /// a header with a line under it. Each cell is VTML and may have a hover.
    /// </summary>
    public sealed class TableBlock : PageBlock
    {
        public double[] Columns = new double[0];
        public List<string[]> Rows = new();
        /// <summary>Per row and column, what hovering the cell shows; null entries (or a null array) for none.</summary>
        public List<string[]> Tips = new();
        public bool HeaderRow = true;
        public override string Key
        {
            get
            {
                var sb = new StringBuilder("TB|");
                foreach (var r in Rows) sb.Append(string.Join("\t", r)).Append('\n');
                foreach (var t in Tips) if (t != null) sb.Append(string.Join("\t", t)).Append('\n');
                return sb.ToString();
            }
        }
    }

    /// <summary>A row of rounded chips that wraps (since v0.3.6i), each VTML with an optional hover.</summary>
    public sealed class ChipsBlock : PageBlock
    {
        public List<(string vtml, string tip)> Chips = new();
        public override string Key
        {
            get
            {
                var sb = new StringBuilder("CH|");
                foreach (var (v, t) in Chips) sb.Append(v).Append('\t').Append(t).Append('\n');
                return sb.ToString();
            }
        }
    }

    /// <summary>A paragraph of VTML, wrapped to the page width.</summary>
    public sealed class TextBlock : PageBlock
    {
        public string Vtml;
        /// <summary>Drawn in the smaller detail font.</summary>
        public bool Small;
        /// <summary>What hovering the paragraph shows (VTML, since v0.3.6); null for no hover.</summary>
        public string Tip;
        /// <summary>What hovering a white "i" drawn right after the paragraph's first line shows (VTML, since 0.4.3b); null for no "i".</summary>
        public string InfoTip;
        public TextBlock(string vtml, bool small = false, string tip = null) { Vtml = vtml; Small = small; Tip = tip; }
        public override string Key => "T|" + (Small ? "s|" : "|") + Vtml + "|" + Tip + "|" + InfoTip;
    }

    /// <summary>
    /// A chart of the last few weeks: a stacked bar a day (calories eaten by food category) on one scale, a line (one
    /// nutrient's reserve) on another, with labels under it and a legend.
    /// </summary>
    public sealed class ChartBlock : PageBlock
    {
        /// <summary>Per day, oldest first: the stacked amounts, one per <see cref="SeriesColors"/> entry.</summary>
        public float[][] Bars = new float[0][];
        public double[][] SeriesColors = new double[0][];
        /// <summary>The bars' top.</summary>
        public float BarMax = 1;
        /// <summary>A dashed line across the bars at this amount (a day's need); 0 for none.</summary>
        public float BarMark;
        /// <summary>Per day, oldest first: the line's value, or null where nothing was recorded.</summary>
        public float?[] Line = new float?[0];
        public float LineMax = 1;
        public double[] LineColor;
        /// <summary>More lines on the same scale as <see cref="Line"/>.</summary>
        public List<(float?[] values, double[] color)> ExtraLines = new();
        /// <summary>VTML under the chart, at the left and right ends.</summary>
        public string BottomLeft, BottomRight;
        /// <summary>Legend entries: a colour swatch and its VTML label.</summary>
        public List<(double[] color, string label)> Legend = new();
        /// <summary>VTML in the plot's top corners (since v0.3.5): the bars' top in yellow on the left, the line's top on the right.</summary>
        public string TopLeft, TopRight;
        /// <summary>Per day, oldest first: what hovering that day shows (VTML), or null for nothing.</summary>
        public string[] Tips;
        /// <summary>What hovering the white "i" at the end of the dashed line shows (VTML); null for no "i".</summary>
        public string MarkTip;

        public const double PlotHeight = 170;

        public override string Key
        {
            get
            {
                var sb = new StringBuilder("C|").Append(BarMax).Append('|').Append(BarMark).Append('|').Append(LineMax).Append('|');
                foreach (var day in Bars) sb.Append(string.Join(",", Array.ConvertAll(day, v => Math.Round(v).ToString()))).Append(';');
                sb.Append('|');
                foreach (var v in Line) sb.Append(v == null ? "-" : Math.Round(v.Value, 1).ToString()).Append(';');
                foreach (var (values, _) in ExtraLines)
                {
                    sb.Append('|');
                    foreach (var v in values) sb.Append(v == null ? "-" : Math.Round(v.Value).ToString()).Append(';');
                }
                sb.Append('|').Append(BottomLeft).Append('|').Append(BottomRight).Append('|').Append(TopLeft).Append('|').Append(TopRight).Append('|').Append(MarkTip).Append('|');
                if (Tips != null) foreach (var t in Tips) sb.Append(t).Append(';');
                foreach (var l in Legend) sb.Append(l.label).Append(';');
                if (LineColor != null) sb.Append(string.Join(",", LineColor));
                return sb.ToString();
            }
        }
    }

    /// <summary>A spot on a drawn page that shows a tooltip when hovered, in pixels from the page's top left.</summary>
    public sealed class Hotspot
    {
        public double X, Y, W, H;
        public string Vtml;
        /// <summary>Lighten the spot while it is hovered (a chart's day).</summary>
        public bool Highlight;
        public bool Contains(double x, double y) => x >= X && x < X + W && y >= Y && y < Y + H;
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
            public GuiElementRichtext Left, Right2;   // chart bottom labels
            public List<(double[] color, GuiElementRichtext text, double x, double y)> Legend;
            public List<(GuiElementRichtext text, double x, double y, double w, double h, string tip)> Cells;   // table cells, chips
            public List<double> RuleY;   // lines under table headers
        }

        private static GuiElementRichtext Rich(ICoreClientAPI capi, string vtml, CairoFont font, double width)
        {
            var bounds = ElementBounds.Fixed(0.0, 0.0, width, 10.0).WithParent(ElementBounds.Empty);
            var text = new GuiElementRichtext(capi, VtmlUtil.Richtextify(capi, vtml ?? "", font), bounds);
            text.CalcHeightAndPositions();
            return text;
        }

        /// <summary>
        /// Draws the page <paramref name="width"/> GUI units wide. The caller disposes the surface. When <paramref name="hotspots"/>
        /// is given, it gets every spot that has a tooltip.
        /// </summary>
        public static ImageSurface Render(ICoreClientAPI capi, IList<PageBlock> blocks, double width, List<Hotspot> hotspots = null)
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
                    case TableBlock tbl:
                    {
                        p.Cells = new List<(GuiElementRichtext, double, double, double, double, string)>();
                        p.RuleY = new List<double>();
                        double ry = 0;
                        for (int r = 0; r < tbl.Rows.Count; r++)
                        {
                            var row = tbl.Rows[r];
                            var tips = r < tbl.Tips.Count ? tbl.Tips[r] : null;
                            double rowH = 0;
                            var cells = new List<(GuiElementRichtext, double, double, double, double, string)>();
                            for (int c = 0; c < row.Length && c < tbl.Columns.Length; c++)
                            {
                                double cx = tbl.Columns[c];
                                double cw = (c + 1 < tbl.Columns.Length ? tbl.Columns[c + 1] : width) - cx - 4;
                                var text = Rich(capi, row[c], detail, Math.Max(10, cw));
                                rowH = Math.Max(rowH, text.TotalHeight);
                                cells.Add((text, GuiElement.scaled(cx), ry, MeasureWidth(text), text.TotalHeight, tips != null && c < tips.Length ? tips[c] : null));
                            }
                            p.Cells.AddRange(cells);
                            ry += rowH + GuiElement.scaled(3);
                            if (r == 0 && tbl.HeaderRow) { p.RuleY.Add(ry - GuiElement.scaled(1)); ry += GuiElement.scaled(3); }
                        }
                        p.Height = ry + GuiElement.scaled(2);
                        break;
                    }
                    case ChipsBlock ch:
                    {
                        p.Cells = new List<(GuiElementRichtext, double, double, double, double, string)>();
                        double cx = 0, cy = GuiElement.scaled(2), rowH = 0, pad = GuiElement.scaled(7);
                        foreach (var (vtml, tip) in ch.Chips)
                        {
                            var text = Rich(capi, vtml, detail, width);
                            double tw = MeasureWidth(text) + 2 * pad;
                            double th = text.TotalHeight + GuiElement.scaled(4);
                            if (cx > 0 && cx + tw > GuiElement.scaled(width)) { cx = 0; cy += rowH + GuiElement.scaled(4); rowH = 0; }
                            p.Cells.Add((text, cx, cy, tw, th, tip));
                            cx += tw + GuiElement.scaled(6);
                            rowH = Math.Max(rowH, th);
                        }
                        p.Height = cy + rowH + GuiElement.scaled(4);
                        break;
                    }
                    case ChartBlock c:
                    {
                        p.Left = Rich(capi, c.BottomLeft, detail, width / 2);
                        p.Right2 = Rich(capi, c.BottomRight, detail, width / 2);
                        double h = GuiElement.scaled(ChartBlock.PlotHeight + 4) + Math.Max(p.Left.TotalHeight, p.Right2.TotalHeight) + GuiElement.scaled(6);
                        // Legend: swatch and label, flowing left to right and wrapping.
                        p.Legend = new List<(double[], GuiElementRichtext, double, double)>();
                        double lx = 0, ly = h, rowH = 0;
                        double swatch = GuiElement.scaled(LegendSwatch);
                        foreach (var (color, label) in c.Legend)
                        {
                            var text = Rich(capi, label, detail, width);
                            double tw = MeasureWidth(text);
                            double ew = swatch + GuiElement.scaled(5) + tw + GuiElement.scaled(14);
                            if (lx > 0 && lx + ew > GuiElement.scaled(width))
                            {
                                lx = 0;
                                ly += rowH + GuiElement.scaled(3);
                                rowH = 0;
                            }
                            p.Legend.Add((color, text, lx, ly));
                            lx += ew;
                            rowH = Math.Max(rowH, text.TotalHeight);
                        }
                        p.Height = ly + rowH + GuiElement.scaled(4);
                        break;
                    }
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
                    if (p.Block is TextBlock tb && !string.IsNullOrEmpty(tb.Tip))
                        hotspots?.Add(new Hotspot { X = 0, Y = p.Y, W = Math.Max(1, MeasureWidth(p.Text)), H = p.Text.TotalHeight, Vtml = tb.Tip });
                    if (p.Block is TextBlock info && !string.IsNullOrEmpty(info.InfoTip))
                    {
                        // The "i" sits just right of the text, centred on its first line.
                        double r = GuiElement.scaled(6);
                        double cx = MeasureWidth(p.Text) + GuiElement.scaled(6) + r;
                        double cy = p.Y + FirstLineHeight(p.Text) / 2.0;
                        DrawInfo(ctx, cx, cy, r);
                        hotspots?.Add(new Hotspot { X = cx - r - 2, Y = cy - r - 2, W = 2 * r + 4, H = 2 * r + 4, Vtml = info.InfoTip });
                    }
                    if (p.Block is TableBlock)
                    {
                        foreach (var ry in p.RuleY)
                        {
                            ctx.SetSourceRGBA(0.85, 0.77, 0.55, 0.35);
                            ctx.Rectangle(0, Math.Round(p.Y + ry), GuiElement.scaled(width), 1);
                            ctx.Fill();
                        }
                        foreach (var (text, x, cy, cw, ch, tip) in p.Cells)
                        {
                            Draw(ctx, surface, text, x, p.Y + cy);
                            if (!string.IsNullOrEmpty(tip)) hotspots?.Add(new Hotspot { X = x, Y = p.Y + cy, W = Math.Max(1, cw), H = ch, Vtml = tip });
                        }
                    }
                    if (p.Block is ChipsBlock)
                    {
                        double pad = GuiElement.scaled(7);
                        foreach (var (text, x, cy, cw, ch, tip) in p.Cells)
                        {
                            GuiElement.RoundRectangle(ctx, x, p.Y + cy, cw, ch, ch / 2);
                            ctx.SetSourceRGBA(0.23, 0.18, 0.14, 1);
                            ctx.FillPreserve();
                            ctx.SetSourceRGBA(0.45, 0.37, 0.27, 1);
                            ctx.LineWidth = 1;
                            ctx.Stroke();
                            Draw(ctx, surface, text, x + pad, p.Y + cy + GuiElement.scaled(2));
                            if (!string.IsNullOrEmpty(tip)) hotspots?.Add(new Hotspot { X = x, Y = p.Y + cy, W = cw, H = ch, Vtml = tip });
                        }
                    }
                    if (p.Block is BarBlock b)
                    {
                        double rowMid = p.Y + GuiElement.scaled(RowHeight) / 2.0;
                        if (!string.IsNullOrEmpty(b.Tip))
                            hotspots?.Add(new Hotspot { X = 0, Y = p.Y, W = GuiElement.scaled(BarX + BarWidth), H = p.Height, Vtml = b.Tip });
                        Draw(ctx, surface, p.Label, 0, rowMid - p.Label.TotalHeight / 2.0);
                        DrawBar(ctx, GuiElement.scaled(BarX), rowMid - GuiElement.scaled(BarHeight) / 2.0, GuiElement.scaled(BarWidth), GuiElement.scaled(BarHeight), b);
                        Draw(ctx, surface, p.Value, GuiElement.scaled(RightX), rowMid - Math.Min(p.Value.TotalHeight, GuiElement.scaled(RowHeight)) / 2.0);
                        if (!string.IsNullOrEmpty(b.RightTip))
                            hotspots?.Add(new Hotspot { X = GuiElement.scaled(RightX), Y = p.Y, W = Math.Max(1, MeasureWidth(p.Value)), H = p.Height, Vtml = b.RightTip });
                        if (b.Trend != null) DrawTrend(ctx, GuiElement.scaled(BarX - 13), rowMid, GuiElement.scaled(9), b.Trend.Value);
                        if (b.Trend != null && !string.IsNullOrEmpty(b.TrendTip))
                        {
                            double hs = GuiElement.scaled(9);
                            hotspots?.Add(new Hotspot { X = GuiElement.scaled(BarX - 13) - hs, Y = rowMid - hs, W = 2 * hs, H = 2 * hs, Vtml = b.TrendTip });
                        }
                    }
                    if (p.Block is ChartBlock c) DrawChart(ctx, surface, p, c, GuiElement.scaled(width), hotspots, capi);
                }
            }
            foreach (var p in placed)
            {
                p.Text?.Dispose();
                p.Label?.Dispose();
                p.Value?.Dispose();
                p.Left?.Dispose();
                p.Right2?.Dispose();
                if (p.Legend != null) foreach (var l in p.Legend) l.text.Dispose();
                if (p.Cells != null) foreach (var c in p.Cells) c.text.Dispose();
            }
            return surface;
        }

        public const double LegendSwatch = 10;
        public static readonly double[] RisingColor = { 0.45, 0.78, 0.40, 1 };
        public static readonly double[] SteadyColor = { 0.70, 0.70, 0.70, 1 };
        public static readonly double[] FallingColor = { 0.91, 0.64, 0.24, 1 };

        /// <summary>The width a rich text line actually takes (its widest line), in pixels.</summary>
        private static double MeasureWidth(GuiElementRichtext text)
        {
            double w = 0;
            foreach (var comp in text.Components)
            {
                if (comp.BoundsPerLine == null) continue;
                foreach (var b in comp.BoundsPerLine) w = Math.Max(w, b.X + b.Width);
            }
            return w;
        }

        /// <summary>The height of a rich text's first line, in pixels (the whole text's height when it has no lines).</summary>
        private static double FirstLineHeight(GuiElementRichtext text)
        {
            foreach (var comp in text.Components)
            {
                if (comp.BoundsPerLine != null && comp.BoundsPerLine.Length > 0) return comp.BoundsPerLine[0].Height;
            }
            return text.TotalHeight;
        }

        /// <summary>A small arrow centred on (cx, cy): up and green when rising, a grey dash when steady, down and amber when falling.</summary>
        private static void DrawTrend(Context ctx, double cx, double cy, double size, int trend)
        {
            double h = size / 2;
            if (trend == 0)
            {
                ctx.SetSourceRGBA(SteadyColor[0], SteadyColor[1], SteadyColor[2], 1);
                ctx.Rectangle(cx - h, cy - size * 0.12, size, size * 0.24);
                ctx.Fill();
                return;
            }
            var c = trend > 0 ? RisingColor : FallingColor;
            double dir = trend > 0 ? -1 : 1;
            ctx.NewPath();
            ctx.MoveTo(cx, cy + dir * h);
            ctx.LineTo(cx - h, cy - dir * h * 0.6);
            ctx.LineTo(cx + h, cy - dir * h * 0.6);
            ctx.ClosePath();
            ctx.SetSourceRGBA(c[0], c[1], c[2], 1);
            ctx.Fill();
        }

        private static void DrawChart(Context ctx, ImageSurface surface, Placed p, ChartBlock c, double width, List<Hotspot> hotspots, ICoreClientAPI capi)
        {
            double top = p.Y + GuiElement.scaled(2);
            double plotH = GuiElement.scaled(ChartBlock.PlotHeight);
            double left = 0.5, right = width - 0.5;
            double bottom = top + plotH;

            // Background and grid.
            ctx.SetSourceRGBA(0, 0, 0, 0.25);
            ctx.Rectangle(left, top, right - left, plotH);
            ctx.Fill();
            ctx.SetSourceRGBA(1, 1, 1, 0.08);
            ctx.LineWidth = 1.0;
            for (int i = 1; i < 4; i++)
            {
                double gy = Math.Round(top + plotH * i / 4.0) + 0.5;
                ctx.MoveTo(left, gy);
                ctx.LineTo(right, gy);
            }
            ctx.Stroke();

            int days = Math.Max(c.Bars.Length, c.Line.Length);
            foreach (var (values, _) in c.ExtraLines) days = Math.Max(days, values.Length);
            if (days > 0)
            {
                double slot = (right - left) / days;
                double barW = Math.Max(1.0, slot * 0.7);

                // Bars, stacked from the bottom.
                for (int d = 0; d < c.Bars.Length; d++)
                {
                    double x = left + slot * d + (slot - barW) / 2;
                    var day = c.Bars[d] ?? new float[0];
                    float max = Math.Max(1e-6f, c.BarMax), cum = 0;
                    for (int s = 0; s < day.Length && s < c.SeriesColors.Length; s++)
                    {
                        if (day[s] <= 0) continue;
                        float from = Math.Min(cum, max), to = Math.Min(cum + day[s], max);
                        cum += day[s];
                        if (to <= from) break;
                        double y0 = bottom - plotH * from / max, y1 = bottom - plotH * to / max;
                        var col = c.SeriesColors[s];
                        ctx.SetSourceRGBA(col[0], col[1], col[2], 0.85);
                        ctx.Rectangle(x, y1, barW, y0 - y1);
                        ctx.Fill();
                    }
                }

                // A day's need, dashed.
                if (c.BarMark > 0 && c.BarMark < c.BarMax)
                {
                    double my = Math.Round(bottom - plotH * c.BarMark / c.BarMax) + 0.5;
                    ctx.SetSourceRGBA(1, 1, 1, 0.35);
                    ctx.SetDash(new double[] { GuiElement.scaled(4), GuiElement.scaled(3) }, 0);
                    ctx.MoveTo(left, my);
                    ctx.LineTo(right, my);
                    ctx.Stroke();
                    ctx.SetDash(new double[0], 0);
                }

                // The lines, broken where a day has no record, with a dot per day.
                double Ly(float v) => bottom - plotH * Math.Clamp(v / Math.Max(1e-6, c.LineMax), 0, 1);
                void Line(float?[] values, double[] lc)
                {
                    lc ??= SteadyColor;
                    bool drawing = false;
                    ctx.NewPath();
                    for (int d = 0; d < values.Length; d++)
                    {
                        var v = values[d];
                        if (v == null)
                        {
                            drawing = false;
                            continue;
                        }
                        double x = left + slot * (d + 0.5);
                        if (drawing) ctx.LineTo(x, Ly(v.Value));
                        else ctx.MoveTo(x, Ly(v.Value));
                        drawing = true;
                    }
                    // A dark outline under the coloured line, so it reads over the bars.
                    ctx.SetSourceRGBA(0, 0, 0, 0.6);
                    ctx.LineWidth = GuiElement.scaled(3.5);
                    ctx.StrokePreserve();
                    ctx.SetSourceRGBA(lc[0], lc[1], lc[2], 1);
                    ctx.LineWidth = GuiElement.scaled(2);
                    ctx.Stroke();
                    for (int d = 0; d < values.Length; d++)
                    {
                        var v = values[d];
                        if (v == null) continue;
                        ctx.Arc(left + slot * (d + 0.5), Ly(v.Value), GuiElement.scaled(2.2), 0, Math.PI * 2);
                        ctx.SetSourceRGBA(lc[0], lc[1], lc[2], 1);
                        ctx.FillPreserve();
                        ctx.SetSourceRGBA(0, 0, 0, 0.6);
                        ctx.LineWidth = 1.0;
                        ctx.Stroke();
                    }
                }
                Line(c.Line, c.LineColor);
                foreach (var (values, color) in c.ExtraLines) Line(values, color);

                // Each day's column shows its tooltip.
                if (hotspots != null && c.Tips != null)
                {
                    for (int d = 0; d < days && d < c.Tips.Length; d++)
                    {
                        if (string.IsNullOrEmpty(c.Tips[d])) continue;
                        hotspots.Add(new Hotspot { X = left + slot * d, Y = top, W = slot, H = plotH, Vtml = c.Tips[d], Highlight = true });
                    }
                }
            }

            // The white "i" on the dashed line, which says what the line is.
            if (c.MarkTip != null && c.BarMark > 0 && c.BarMark < c.BarMax)
            {
                double r = GuiElement.scaled(6);
                // In the leftmost day's column (28 days ago), where it is least in the way of recent days.
                double cx = left + Math.Max(r + GuiElement.scaled(3), (right - left) / Math.Max(1, days) / 2);
                double cy = Math.Clamp(bottom - plotH * c.BarMark / c.BarMax, top + r + 1, bottom - r - 1);
                DrawInfo(ctx, cx, cy, r);
                // Listed after the days so it wins where they overlap.
                hotspots?.Add(new Hotspot { X = cx - r - 2, Y = cy - r - 2, W = 2 * r + 4, H = 2 * r + 4, Vtml = c.MarkTip });
            }

            if (capi != null)
            {
                var detail = CairoFont.WhiteDetailText();
                double pad = GuiElement.scaled(4);
                if (!string.IsNullOrEmpty(c.TopLeft))
                {
                    using var tl = Rich(capi, c.TopLeft, detail, width / 2);
                    Draw(ctx, surface, tl, left + pad, top + GuiElement.scaled(2));
                }
                if (!string.IsNullOrEmpty(c.TopRight))
                {
                    using var tr = Rich(capi, c.TopRight, detail, width / 2);
                    Draw(ctx, surface, tr, right - pad - MeasureWidth(tr), top + GuiElement.scaled(2));
                }
            }

            ctx.SetSourceRGBA(0, 0, 0, 0.6);
            ctx.LineWidth = 1.0;
            ctx.Rectangle(left, top, right - left, plotH);
            ctx.Stroke();

            double labelY = bottom + GuiElement.scaled(4);
            Draw(ctx, surface, p.Left, 0, labelY);
            Draw(ctx, surface, p.Right2, width - MeasureWidth(p.Right2), labelY);

            double sw = GuiElement.scaled(LegendSwatch);
            foreach (var (color, text, x, y) in p.Legend)
            {
                double ty = p.Y + y;
                ctx.SetSourceRGBA(color[0], color[1], color[2], 1);
                ctx.Rectangle(x, ty + (text.TotalHeight - sw) / 2, sw, sw);
                ctx.FillPreserve();
                ctx.SetSourceRGBA(0, 0, 0, 0.6);
                ctx.LineWidth = 1.0;
                ctx.Stroke();
                Draw(ctx, surface, text, x + sw + GuiElement.scaled(5), ty);
            }
        }

        /// <summary>A white "i" in a circle, centred on (cx, cy), with a dark outline so it reads over the bars.</summary>
        private static void DrawInfo(Context ctx, double cx, double cy, double r)
        {
            ctx.NewPath();
            ctx.Arc(cx, cy, r, 0, Math.PI * 2);
            ctx.SetSourceRGBA(0.95, 0.95, 0.95, 1);
            ctx.FillPreserve();
            ctx.SetSourceRGBA(0, 0, 0, 0.7);
            ctx.LineWidth = 1.0;
            ctx.Stroke();
            // The letter: a dot and a stem, drawn so no font is needed.
            ctx.SetSourceRGBA(0.12, 0.12, 0.12, 1);
            double w = Math.Max(1.5, r * 0.32);
            ctx.Arc(cx, cy - r * 0.48, w * 0.62, 0, Math.PI * 2);
            ctx.Fill();
            ctx.Rectangle(cx - w / 2, cy - r * 0.18, w, r * 0.82);
            ctx.Fill();
        }

        /// <summary>
        /// A tooltip as the game draws its hovers: the VTML on a dark rounded box, at most <paramref name="maxWidth"/> GUI units
        /// wide. The caller disposes the surface.
        /// </summary>
        public static ImageSurface RenderTip(ICoreClientAPI capi, string vtml, double maxWidth)
        {
            var font = CairoFont.WhiteSmallText().WithFontSize(14);
            using var text = Rich(capi, vtml, font, maxWidth);
            double pad = GuiElement.scaled(7);
            double tw = Math.Min(GuiElement.scaled(maxWidth), MeasureWidth(text));
            int w = (int)Math.Ceiling(tw + 2 * pad), h = (int)Math.Ceiling(text.TotalHeight + 2 * pad);
            var surface = new ImageSurface(Format.Argb32, Math.Max(1, w), Math.Max(1, h));
            using (var ctx = new Context(surface))
            {
                GuiElement.RoundRectangle(ctx, 0.5, 0.5, w - 1, h - 1, GuiElement.scaled(3));
                ctx.SetSourceRGBA(GuiStyle.DialogStrongBgColor[0], GuiStyle.DialogStrongBgColor[1], GuiStyle.DialogStrongBgColor[2], 0.96);
                ctx.FillPreserve();
                ctx.SetSourceRGBA(GuiStyle.DialogLightBgColor[0] * 1.6, GuiStyle.DialogLightBgColor[1] * 1.6, GuiStyle.DialogLightBgColor[2] * 1.6, 1);
                ctx.LineWidth = 1.0;
                ctx.Stroke();
                Draw(ctx, surface, text, pad, pad);
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
