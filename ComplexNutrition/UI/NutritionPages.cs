using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ComplexNutrition.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace ComplexNutrition.UI
{
    /// <summary>A food the player has held, with its display name.</summary>
    public sealed class KnownFood
    {
        public string Name;
        public ResolvedFoodProfile Food;
    }

    /// <summary>Everything a nutrition window page shows, gathered on the client (or made up, for previews).</summary>
    public sealed class NutritionPageData
    {
        public NutrientRegistry Nutrients;
        public DigestionSettings Digest;
        public TrackingSettings Tracking;
        /// <summary>The player's state, or null while the server does not track them.</summary>
        public NutritionState State;
        public float Fullness;
        public float MaxFullness;
        public float Health;
        public float MaxHealth;
        public bool ShowPercent;
        public IReadOnlyList<KnownFood> Known = new List<KnownFood>();
        /// <summary>The last history the server sent, or null (not yet).</summary>
        public HistoryData History;
    }

    /// <summary>
    /// Builds the nutrition window's tabs as page blocks: Overview, Digestion, Vitamins, Minerals and (v0.3.2) History. Pure text and numbers, so previews can build them outside the game.
    /// </summary>
    public static class NutritionPages
    {
        public const string Low = "#e8a33c";
        public const string Empty = "#ec6b5e";
        public static readonly double[] FullnessColor = { 0.62, 0.52, 0.36, 1.0 };

        private static string L(string key, params object[] args) => Lang.Get("complexnutrition:" + key, args);

        private static double[] ColorOf(NutrientDefinition n) => NutrientDisplay.ColorOf(n);

        public static string Num(float v) => Fmt.Number(v);

        /// <summary>An amount of a nutrient, in its unit (g for the macros, kcal for calories), shown as the reader chose: amount or percent of a day.</summary>
        private static string Amt(NutritionPageData d, string code, float amount) =>
            d.Nutrients.Get(code)?.FormatAmount(amount, d.ShowPercent) ?? Num(amount);

        private static string Kcal(NutritionPageData d, float kcal) => Amt(d, NutrientCodes.Calories, kcal);

        /// <summary>The calories per hour fiber adds right now, its rate in red like the gut lines.</summary>
        private static string FiberBoost(NutritionPageData d)
        {
            float kcal = Digestion.FiberKcalPerHour(d.Digest, d.State, d.Nutrients);
            return kcal < 0.05f
                ? StatsHover.Text(L("fiber-boost-none"))
                : StatsHover.Vtml("complexnutrition:fiber-boost", StatsHover.Paint(Kcal(d, kcal), UnitColors.Rate));
        }

        /// <summary>The calories still to come from the gut, and how many are lost to diminishing returns.</summary>
        private static string GutOutlookText(NutritionPageData d)
        {
            var (coming, lost) = Digestion.GutOutlook(d.Digest, d.State, d.Nutrients);
            if (coming < 0.5f && lost < 0.5f) return StatsHover.Text(L("window-outlook-empty"));
            string text = L("window-outlook", Kcal(d, coming));
            text += " " + (lost < 0.5f ? L("window-outlook-nolost") : L("window-outlook-lost", Kcal(d, lost)));
            return StatsHover.Text(text);
        }

        private static List<PageBlock> Off() => new()
        {
            new GapBlock(6),
            new TextBlock(StatsHover.Text(L("window-tracking-off"))),
        };

        // ---- Shared pieces ----

        /// <summary>Days of a reserve, coloured amber when it is running low and red when it is nearly gone.</summary>
        private static string DaysVtml(NutritionPageData d, NutrientDefinition n, float reserve)
        {
            string text = StatsHover.Text(NutrientDisplay.DaysText(reserve));
            float days = reserve / 100f;
            if (n.IsCalories)
            {
                if (days < d.Tracking.LowCaloriesDays) return "<font color=\"" + Empty + "\">" + text + "</font>";
                return text;
            }
            if (!CanRunLow(d, n)) return text;
            if (days < 0.5f) return "<font color=\"" + Empty + "\">" + text + "</font>";
            if (days < d.Tracking.LowDays) return "<font color=\"" + Low + "\">" + text + "</font>";
            return text;
        }

        private static bool CanRunLow(NutritionPageData d, NutrientDefinition n) =>
            n.IsCalories || (d.Tracking.LowGroups?.Contains(n.Group) ?? false);

        /// <summary>Nutrients running low, lowest first: the calorie tank under its line, and the slow nutrients under theirs.</summary>
        public static List<NutrientDefinition> RunningLow(NutritionPageData d)
        {
            var low = new List<(NutrientDefinition n, float days)>();
            if (d.State == null) return new List<NutrientDefinition>();
            foreach (var n in d.Nutrients.All)
            {
                if (!CanRunLow(d, n)) continue;
                float days = d.State.GetReserve(n.Code) / 100f;
                float line = n.IsCalories ? d.Tracking.LowCaloriesDays : d.Tracking.LowDays;
                if (days < line) low.Add((n, days / Math.Max(0.01f, line)));
            }
            return low.OrderBy(t => t.days).Select(t => t.n).ToList();
        }

        /// <summary>A food's amount of a nutrient per portion, in the nutrient's unit (calories as the body gets them from the macros).</summary>
        private static float AmountIn(NutritionPageData d, ResolvedFoodProfile food, NutrientDefinition n)
        {
            if (n.IsCalories && d.Digest != null) return d.Digest.KcalFromMacros(food.PerPortion);
            return food.PerPortion(n.Code);
        }

        /// <summary>Up to three foods the player has held that are richest in this nutrient, as "Carrot (450mcg)".</summary>
        public static string BestFoods(NutritionPageData d, NutrientDefinition n, int count = 3)
        {
            var best = d.Known
                .Select(k => (k.Name, amount: AmountIn(d, k.Food, n)))
                .Where(t => n.ToPercent(t.amount) >= 5f)
                .GroupBy(t => t.Name).Select(g => g.OrderByDescending(t => t.amount).First())
                .OrderByDescending(t => t.amount)
                .Take(count)
                .Select(t => StatsHover.Esc(t.Name) + " (" + StatsHover.Text(n.FormatAmount(t.amount, d.ShowPercent)) + ")")
                .ToList();
            return best.Count == 0
                ? "<font color=\"" + StatsHover.NoteColor + "\">" + StatsHover.Esc(L("window-no-foods")) + "</font>"
                : StatsHover.Vtml("complexnutrition:window-best-foods", string.Join(", ", best));
        }

        private static BarBlock ReserveBar(NutritionPageData d, NutrientDefinition n, string label = null)
        {
            float r = d.State.GetReserve(n.Code);
            return new BarBlock
            {
                Label = StatsHover.Esc(label ?? Lang.Get(n.LangKey)),
                Value = r,
                Max = Math.Max(n.Cap, r),
                LineInterval = 100,
                Color = ColorOf(n),
                Right = StatsHover.Vtml("complexnutrition:reserve-hover", StatsHover.Text(n.Format(r, d.ShowPercent)), DaysVtml(d, n, r)),
            };
        }

        private static BarBlock FullnessBar(NutritionPageData d) => new()
        {
            Label = StatsHover.Esc(L("stats-fullness")),
            Value = d.Fullness,
            Max = Math.Max(1f, d.MaxFullness),
            LineInterval = 100,
            Color = FullnessColor,
            Right = StatsHover.Text(L("window-fullness-value", (int)Math.Round(d.Fullness), (int)Math.Round(d.MaxFullness))),
        };


        // ---- Overview ----

        public static List<PageBlock> Overview(NutritionPageData d)
        {
            if (d.State == null) return Off();
            var blocks = new List<PageBlock>();
            var cal = d.Nutrients.Get(NutrientCodes.Calories);

            blocks.Add(new HeadingBlock(L("window-heading-energy")));
            if (cal != null)
            {
                var bar = ReserveBar(d, cal);
                bar.Max = Math.Max(cal.Cap, d.State.GetReserve(cal.Code));
                blocks.Add(bar);
            }
            blocks.Add(FullnessBar(d));
            blocks.Add(new TextBlock(GutOutlookText(d), true));
            if (d.History != null)
            {
                float week = d.History.Days.Skip(Math.Max(0, d.History.Days.Count - 7)).Sum(day => day.Burned);
                blocks.Add(new TextBlock(StatsHover.Text(L("window-burned", Kcal(d, d.History.BurnedHour), Kcal(d, d.History.Burned24), Kcal(d, week))), true));
            }
            if (d.MaxHealth > 0 && d.Health < d.MaxHealth - 0.01f && d.Tracking != null)
            {
                float perHp = d.Tracking.KcalPerHp((d.MaxHealth - d.Health) / d.MaxHealth);
                float toFull = d.Tracking.KcalToHeal(d.Health, d.MaxHealth);
                blocks.Add(new TextBlock(StatsHover.Text(L("window-healing", Kcal(d, perHp), Kcal(d, toFull))), true));
                blocks.Add(new TextBlock(L("window-healing-tip"), true));
            }

            // How close the rolling totals are to where calories start falling off.
            var s = d.Digest;
            if (s != null)
            {
                blocks.Add(new HeadingBlock(L("window-heading-macros")));
                foreach (var code in new[] { NutrientCodes.Carbohydrate, NutrientCodes.Fat })
                {
                    var pool = s.Pool(code);
                    var n = d.Nutrients.Get(code);
                    if (pool == null || n == null) continue;
                    float window = Digestion.WindowGrams(d.State, code, pool);
                    blocks.Add(new BarBlock
                    {
                        Label = StatsHover.Esc(L("window-macro-label", Lang.Get(n.LangKey), pool.WindowHours)),
                        Value = window,
                        Max = Math.Max(pool.DiminishingAboveGrams, window),
                        Color = ColorOf(n),
                        Right = StatsHover.Text(L("window-macro-value", Amt(d, code, window), Amt(d, code, pool.DiminishingAboveGrams))),
                    });
                }
                var protein = d.Nutrients.Get(NutrientCodes.Protein);
                if (protein != null)
                {
                    float p24 = d.State.Protein24;
                    blocks.Add(new BarBlock
                    {
                        Label = StatsHover.Esc(L("window-macro-label", Lang.Get(protein.LangKey), 24)),
                        Value = p24,
                        Max = Math.Max(s.ProteinMaxGrams, p24),
                        LineInterval = s.ProteinNeedGrams,
                        Color = ColorOf(protein),
                        Right = StatsHover.Text(L("window-protein-value", Amt(d, protein.Code, p24), Amt(d, protein.Code, s.ProteinNeedGrams))),
                    });
                }
                blocks.Add(new TextBlock(StatsHover.Text(L("window-macros-note", Amt(d, NutrientCodes.Protein, s.ProteinMaxGrams))), true));
                blocks.Add(new TextBlock(FiberBoost(d), true));
            }

            blocks.Add(new HeadingBlock(L("window-heading-low")));
            var low = RunningLow(d);
            if (low.Count == 0)
            {
                blocks.Add(new TextBlock(StatsHover.Text(L("window-low-none"))));
            }
            else
            {
                foreach (var n in low.Take(3))
                {
                    blocks.Add(ReserveBar(d, n));
                    blocks.Add(new TextBlock(BestFoods(d, n), true));
                }
            }

            blocks.Add(new HeadingBlock(L("window-heading-diet")));
            blocks.Add(new TextBlock(StatsHover.Text(low.Count == 0
                ? L("window-diet-fine")
                : L("window-diet-short", JoinNames(low.Select(n => Lang.Get(n.LangKey)).ToList())))));
            return blocks;
        }

        /// <summary>"vitamin A", "vitamin A and calcium", "vitamin A, iron and calcium". Calories stay capitalised as a heading word.</summary>
        public static string JoinNames(IList<string> names)
        {
            var lower = names.Select(n => n.StartsWith("Vitamin ") ? "v" + n.Substring(1) : n.StartsWith("B vitamins") ? "the B vitamins" : n.ToLowerInvariant()).ToList();
            if (lower.Count == 1) return lower[0];
            return string.Join(", ", lower.Take(lower.Count - 1)) + " " + L("window-and") + " " + lower[^1];
        }

        // ---- Digestion ----

        public static List<PageBlock> DigestionPage(NutritionPageData d)
        {
            if (d.State == null) return Off();
            var blocks = new List<PageBlock>();
            var s = d.Digest;

            blocks.Add(new HeadingBlock(L("window-heading-stomach")));
            blocks.Add(FullnessBar(d));
            if (s != null)
            {
                float hours = s.FullnessDrainPerHour > 0 ? d.Fullness / s.FullnessDrainPerHour : 0f;
                blocks.Add(new TextBlock(StatsHover.Text(d.Fullness < 1f
                    ? L("window-stomach-empty", Num(s.FullnessDrainPerHour))
                    : L("window-stomach-text", Num(s.FullnessDrainPerHour), hours.ToString(hours < 10 ? "0.0" : "0", CultureInfo.InvariantCulture))), true));
            }
            if (s == null) return blocks;

            blocks.Add(new HeadingBlock(L("window-heading-gut")));
            foreach (var code in new[] { NutrientCodes.Carbohydrate, NutrientCodes.Fat, NutrientCodes.Protein })
            {
                var pool = s.Pool(code);
                var n = d.Nutrients.Get(code);
                if (pool == null || n == null) continue;
                float reserve = d.State.GetReserve(code);
                float grams = reserve / 100f * n.DailyTarget;
                blocks.Add(new BarBlock
                {
                    Label = StatsHover.Esc(Lang.Get(n.LangKey)),
                    Value = grams,
                    Max = Math.Max(pool.MaxGrams, grams),
                    LineInterval = n.DailyTarget,
                    Color = ColorOf(n),
                    Right = StatsHover.Text(L("window-gut-value", Amt(d, code, grams), Amt(d, code, pool.MaxGrams))),
                });

                float window = Digestion.WindowGrams(d.State, code, pool);
                if (grams > DigestionPool.EmptyGrams)
                {
                    float speed = Digestion.FiberSpeed(s, d.State, pool);
                    float kcal = pool.Effective(window, grams) * pool.KcalPerGram;
                    float rate = pool.Rate(grams, speed);
                    float kcalRate = pool.Effective(window, rate) * pool.KcalPerGram;
                    blocks.Add(new TextBlock(StatsHover.Vtml("complexnutrition:window-gut-detail",
                        StatsHover.Text(Kcal(d, kcal)),
                        StatsHover.Text(NutrientDisplay.GutHoursText(d.State.GetGutHours(code), reserve)),
                        StatsHover.Paint(L("gut-rate", Amt(d, code, rate), Kcal(d, kcalRate)), UnitColors.Rate)), true));
                }
                else
                {
                    blocks.Add(new TextBlock(StatsHover.Text(L("window-gut-empty")), true));
                }

                string limits = code == NutrientCodes.Protein
                    ? L("window-gut-window-protein", Amt(d, code, d.State.Protein24), Amt(d, code, s.ProteinNeedGrams), Amt(d, code, s.ProteinMaxGrams))
                    : L("window-gut-window", pool.WindowHours, Amt(d, code, window), Amt(d, code, pool.DiminishingAboveGrams));
                blocks.Add(new TextBlock(StatsHover.Text(limits), true));
                blocks.Add(new GapBlock(4));
            }

            var fiber = d.Nutrients.Get(NutrientCodes.Fiber);
            if (fiber != null)
            {
                blocks.Add(new HeadingBlock(Lang.Get(fiber.LangKey)));
                blocks.Add(ReserveBar(d, fiber));
                string Bonus(string code)
                {
                    var pool = s.Pool(code);
                    return pool == null ? "0" : Math.Round((Digestion.FiberSpeed(s, d.State, pool) - 1f) * 100f).ToString("0", CultureInfo.InvariantCulture);
                }
                blocks.Add(new TextBlock(StatsHover.Text(L("window-fiber-speed", Bonus(NutrientCodes.Carbohydrate), Bonus(NutrientCodes.Fat), Bonus(NutrientCodes.Protein),
                    fiber.Format(s.FiberBonusFullAt, d.ShowPercent))), true));
            }
            return blocks;
        }

        // ---- Vitamins and minerals ----

        public static List<PageBlock> Vitamins(NutritionPageData d) =>
            Group(d, n => n.Group == "WaterSolubleVitamin" || n.Group == "FatSolubleVitamin", "window-intro-vitamins");

        public static List<PageBlock> Minerals(NutritionPageData d) =>
            Group(d, n => n.Group == "Mineral", "window-intro-minerals");

        private static List<PageBlock> Group(NutritionPageData d, Func<NutrientDefinition, bool> which, string introKey)
        {
            if (d.State == null) return Off();
            var blocks = new List<PageBlock> { new TextBlock(StatsHover.Text(L(introKey)), true) };
            if (d.History?.DayAgo != null) blocks.Add(new TextBlock(StatsHover.Text(L("window-trend-note")), true));
            blocks.Add(new GapBlock(2));
            foreach (var n in d.Nutrients.All.Where(which))
            {
                blocks.Add(new HeadingBlock(Lang.Get(n.LangKey)));
                var bar = ReserveBar(d, n, L("window-stored"));
                bar.Trend = TrendOf(d, n);
                blocks.Add(bar);
                blocks.Add(new TextBlock(StatsHover.Text(Lang.Get("complexnutrition:nutrient-" + n.Code + "-role")), true));
                blocks.Add(new TextBlock(BestFoods(d, n), true));
            }
            return blocks;
        }

        /// <summary>Whether a store rose, held or fell over about the last day; null until the server has a reading that old.</summary>
        public static int? TrendOf(NutritionPageData d, NutrientDefinition n)
        {
            var h = d.History;
            if (h?.DayAgo == null || d.State == null || !h.DayAgo.TryGetValue(n.Code, out float then)) return null;
            return NutrientHistory.Trend(then, d.State.GetReserve(n.Code), h.DayAgoHours);
        }

        // ---- History ----

        /// <summary>The vanilla food categories the diet ledger records, in the order the bars stack; anything else counts as Unknown.</summary>
        public static readonly string[] Categories = { "Fruit", "Vegetable", "Protein", "Grain", "Dairy", "Unknown" };

        public static readonly Dictionary<string, double[]> CategoryColors = new()
        {
            ["Fruit"] = new[] { 0.80, 0.33, 0.40, 1.0 },
            ["Vegetable"] = new[] { 0.42, 0.66, 0.32, 1.0 },
            ["Protein"] = new[] { 0.66, 0.42, 0.28, 1.0 },
            ["Grain"] = new[] { 0.86, 0.71, 0.36, 1.0 },
            ["Dairy"] = new[] { 0.85, 0.84, 0.74, 1.0 },
            ["Unknown"] = new[] { 0.52, 0.52, 0.52, 1.0 },
        };

        /// <summary>The nutrients the History tab can draw: the calorie tank, fiber, and every vitamin and mineral (gut pools only hold food on its way through).</summary>
        public static List<NutrientDefinition> HistoryNutrients(NutrientRegistry nutrients) =>
            nutrients.All.Where(n => n.IsCalories || n.Code == NutrientCodes.Fiber || n.IsMicronutrient).ToList();

        private static float[] Stack(HistoryDay day)
        {
            var result = new float[Categories.Length];
            foreach (var kv in day.KcalByCategory)
            {
                int i = Array.IndexOf(Categories, kv.Key);
                if (i < 0) i = Categories.Length - 1;
                result[i] += Math.Max(0f, kv.Value);
            }
            return result;
        }

        /// <summary>A round number at or above <paramref name="v"/>: 1, 2 or 5 times a power of ten.</summary>
        public static float NiceCeiling(float v)
        {
            if (v <= 0) return 1;
            double p = Math.Pow(10, Math.Floor(Math.Log10(v)));
            foreach (var m in new[] { 1.0, 2.0, 2.5, 5.0, 10.0 })
            {
                if (m * p >= v - 1e-6) return (float)(m * p);
            }
            return (float)(10 * p);
        }

        /// <summary>The History tab's line, white so it stands apart from every food group's colour.</summary>
        public static readonly double[] LineColor = { 0.95, 0.95, 0.95, 1.0 };
        public static readonly double[] GainedColor = { 0.45, 0.78, 0.40, 1.0 };
        public static readonly double[] BurnedColor = { 0.93, 0.42, 0.37, 1.0 };

        /// <summary>The History tab: the last weeks of calories eaten (bars, by food category) and one nutrient's store (the line).</summary>
        public static List<PageBlock> History(NutritionPageData d, string code)
        {
            if (d.State == null) return Off();
            var blocks = new List<PageBlock>();
            var h = d.History;
            if (h == null || h.Days == null || h.Days.Count == 0)
            {
                blocks.Add(new GapBlock(6));
                blocks.Add(new TextBlock(StatsHover.Text(L("history-loading"))));
                return blocks;
            }

            var n = d.Nutrients.Get(code) ?? d.Nutrients.Get(NutrientCodes.Calories);
            var cal = d.Nutrients.Get(NutrientCodes.Calories);
            if (n == null || cal == null) return blocks;
            string name = Lang.Get(n.LangKey);

            var stacks = h.Days.Select(Stack).ToArray();
            float most = stacks.Length == 0 ? 0 : stacks.Max(st => st.Sum());
            float barTop = Math.Max(most, cal.DailyTarget * 1.25f);
            // A round top in the units the reader sees.
            float barMax = d.ShowPercent ? NiceCeiling(cal.ToPercent(barTop)) / 100f * cal.DailyTarget : NiceCeiling(barTop);
            var line = h.Days.Select(day => day.Reserves != null && day.Reserves.TryGetValue(n.Code, out float r) ? (float?)r : null).ToArray();
            float lineTop = line.Where(v => v != null).Select(v => v.Value).DefaultIfEmpty(0).Max();
            float lineMax = Math.Max(n.Cap, lineTop);

            blocks.Add(new TextBlock(n.IsCalories
                ? StatsHover.Text(L("history-intro-calories", h.Days.Count))
                : StatsHover.Text(L("history-intro", h.Days.Count, JoinNames(new[] { name }))), true));
            blocks.Add(new TextBlock(StatsHover.Text(L("history-scale", Kcal(d, barMax), Kcal(d, cal.DailyTarget), NutrientDisplay.DaysText(lineMax))), true));
            blocks.Add(new GapBlock(4));

            var chart = new ChartBlock
            {
                Bars = stacks,
                SeriesColors = Categories.Select(c => CategoryColors[c]).ToArray(),
                BarMax = barMax,
                BarMark = cal.DailyTarget,
                Line = line,
                LineMax = lineMax,
                LineColor = LineColor,
                BottomLeft = StatsHover.Text(L("history-days-ago", h.Days.Count - 1)),
                BottomRight = StatsHover.Text(L("history-today")),
            };
            for (int i = 0; i < Categories.Length; i++)
            {
                bool any = stacks.Any(st => st[i] > 0);
                if (Categories[i] == "Unknown" && !any) continue;
                chart.Legend.Add((CategoryColors[Categories[i]], StatsHover.Esc(L("history-cat-" + Categories[i].ToLowerInvariant()))));
            }
            chart.Legend.Add((chart.LineColor, StatsHover.Esc(n.IsCalories ? L("history-line-legend-calories") : L("history-line-legend", name))));
            blocks.Add(chart);

            // Calories into and out of the tank.
            var gained = h.Days.Select(day => day.Gained > 0 || day.Burned > 0 ? (float?)day.Gained : null).ToArray();
            var burned = h.Days.Select(day => day.Gained > 0 || day.Burned > 0 ? (float?)day.Burned : null).ToArray();
            float flowTop = h.Days.Select(day => Math.Max(day.Gained, day.Burned)).DefaultIfEmpty(0).Max();
            float flowMax = d.ShowPercent ? NiceCeiling(cal.ToPercent(Math.Max(flowTop, cal.DailyTarget * 1.25f))) / 100f * cal.DailyTarget : NiceCeiling(Math.Max(flowTop, cal.DailyTarget * 1.25f));
            blocks.Add(new HeadingBlock(L("history-heading-flow")));
            blocks.Add(new TextBlock(StatsHover.Text(L("history-flow-intro", Kcal(d, flowMax))), true));
            blocks.Add(new GapBlock(4));
            blocks.Add(new ChartBlock
            {
                BarMax = flowMax,
                BarMark = cal.DailyTarget,
                Line = gained,
                LineMax = flowMax,
                LineColor = GainedColor,
                ExtraLines = { (burned, BurnedColor) },
                BottomLeft = StatsHover.Text(L("history-days-ago", h.Days.Count - 1)),
                BottomRight = StatsHover.Text(L("history-today")),
                Legend = { (GainedColor, StatsHover.Esc(L("history-gained"))), (BurnedColor, StatsHover.Esc(L("history-burned"))) },
            });

            // The last week in words.
            blocks.Add(new HeadingBlock(L("history-heading-week")));
            var week = h.Days.Skip(Math.Max(0, h.Days.Count - 8)).Take(Math.Min(7, Math.Max(0, h.Days.Count - 1))).ToList();
            var eaten = week.Where(day => day.TotalKcal > 0).ToList();
            if (eaten.Count == 0)
            {
                blocks.Add(new TextBlock(StatsHover.Text(L("history-week-none"))));
            }
            else
            {
                float avg = week.Sum(day => day.TotalKcal) / week.Count;
                var totals = new float[Categories.Length];
                foreach (var day in week)
                {
                    var st = Stack(day);
                    for (int i = 0; i < totals.Length; i++) totals[i] += st[i];
                }
                int top = Array.IndexOf(totals, totals.Max());
                float share = totals.Sum() > 0 ? totals[top] / totals.Sum() * 100f : 0f;
                blocks.Add(new TextBlock(StatsHover.Text(L("history-week-eaten", Kcal(d, avg), week.Count,
                    Math.Round(share).ToString("0", CultureInfo.InvariantCulture), L("history-from-" + Categories[top].ToLowerInvariant())))));
            }

            var flowWeek = week.Where(day => day.Gained > 0 || day.Burned > 0).ToList();
            if (flowWeek.Count > 0)
            {
                blocks.Add(new TextBlock(StatsHover.Text(L("history-week-flow", Kcal(d, flowWeek.Average(day => day.Gained)), Kcal(d, flowWeek.Average(day => day.Burned)), flowWeek.Count))));
            }

            float now = d.State.GetReserve(n.Code);
            if (n.IsCalories) name = L("history-line-legend-calories");
            float? weekAgo = h.Days.Count >= 8 ? line[h.Days.Count - 8] : null;
            blocks.Add(new TextBlock(StatsHover.Text(weekAgo == null
                ? L("history-store-now", name, NutrientDisplay.DaysText(now))
                : L("history-store-week", name, NutrientDisplay.DaysText(now), NutrientDisplay.DaysText(weekAgo.Value)))));
            return blocks;
        }
    }
}
