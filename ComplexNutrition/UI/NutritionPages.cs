using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
            if (DigestionPool.DiminishingReturns) text += " " + (lost < 0.5f ? L("window-outlook-nolost") : L("window-outlook-lost", Kcal(d, lost)));
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

        /// <summary>Nutrients running low, lowest first: the calorie reserve under its line, and the slow nutrients under theirs.</summary>
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

        /// <summary>Each macro's own colour (v0.3.6i), so carbs, fats, protein and fiber can be told apart at a glance.</summary>

        private static double[] MacroColor(NutrientDefinition n) => ColorOf(n);

        private static readonly string[] Macros = { NutrientCodes.Carbohydrate, NutrientCodes.Fat, NutrientCodes.Protein, NutrientCodes.Fiber };

        private static string Grey(string text) => "<font color=\"#a0a0a0\">" + StatsHover.Text(text) + "</font>";

        public static List<PageBlock> Overview(NutritionPageData d)
        {
            if (d.State == null) return Off();
            var blocks = new List<PageBlock>();
            var cal = d.Nutrients.Get(NutrientCodes.Calories);

            blocks.Add(new HeadingBlock(L("window-heading-energy")));
            if (cal != null)
            {
                var bar = ReserveBar(d, cal, L("window-calorie-reserve"));
                bar.Max = Math.Max(cal.Cap, d.State.GetReserve(cal.Code));
                // What is on its way and what was burned live in the arrow's hover.
                bar.Trend = TrendOf(d, cal) ?? 0;
                string tip = GutOutlookText(d);
                if (d.History != null)
                {
                    float week = d.History.Days.Skip(Math.Max(0, d.History.Days.Count - 7)).Sum(day => day.Burned);
                    tip += "<br><br>" + StatsHover.Text(L("window-burned", Kcal(d, d.History.BurnedHour), Kcal(d, d.History.Burned24), Kcal(d, week)));
                }
                bar.TrendTip = tip;
                blocks.Add(bar);
            }
            // Fullness only ever goes down, so it has no arrow (v0.3.6i); how fast is in the value's hover.
            var full = FullnessBar(d);
            full.RightTip = StatsHover.Text(L("window-fullness-trend", Num(d.Digest?.FullnessDrainPerHour ?? 100f)));
            blocks.Add(full);
            if (d.MaxHealth > 0 && d.Health < d.MaxHealth - 0.01f && d.Tracking != null)
            {
                float perHp = d.Tracking.KcalPerHp((d.MaxHealth - d.Health) / d.MaxHealth);
                float toFull = d.Tracking.KcalToHeal(d.Health, d.MaxHealth);
                blocks.Add(new TextBlock(StatsHover.Text(L("window-healing", Kcal(d, perHp), Kcal(d, toFull))), true));
                blocks.Add(new TextBlock(L("window-healing-tip"), true));
            }

            // The last 24 hours of each macro as one table (v0.3.6i): digested, the limit, and the calories it gave.
            var s = d.Digest;
            if (s != null)
            {
                blocks.Add(new HeadingBlock(L("window-heading-last24")));
                var table = new TableBlock { Columns = new[] { 0.0, 120.0, 215.0, 310.0 } };
                string Head(string key) => "<font color=\"#c8b48c\">" + StatsHover.Esc(L(key)) + "</font>";
                table.Rows.Add(new[] { "", Head("window-col-digested"), Head("window-col-limit"), Head("window-col-gave") });
                table.Tips.Add(null);
                float total = 0f;
                foreach (var code in Macros)
                {
                    var n = d.Nutrients.Get(code);
                    if (n == null) continue;
                    var pool = s.Pool(code);
                    float limit;
                    if (code == NutrientCodes.Fiber) limit = DigestionSettings.FiberGutRate(n) * 24f;
                    else if (pool == null) continue;
                    // Carbs' 12-hour limit, scaled to the 24 hours shown (300g -> 600g); a visual match only.
                    else limit = code == NutrientCodes.Protein ? s.ProteinNeedGrams : pool.DiminishingAboveGrams * 24f / Math.Clamp(pool.WindowHours, 1, 24);
                    float grams = Digestion.Last24(d.State, code);
                    float kcal = Digestion.Last24(d.State, Digestion.KcalLogKey(code));
                    float lost = pool != null && kcal > 0f && DigestionPool.DiminishingReturns ? Math.Max(0f, grams * pool.KcalPerGram - kcal) : 0f;
                    if (lost < 0.5f) lost = 0f;
                    total += kcal;
                    string name = Lang.Get(n.LangKey);
                    table.Rows.Add(new[] { StatsHover.Esc(name), StatsHover.Text(Amt(d, code, grams)), StatsHover.Text(Amt(d, code, limit)), KcalCell(d, kcal, lost) });
                    table.Tips.Add(new[] { NutrientTip(d, n), null, null, KcalTip(d, name, kcal, lost) });
                }
                table.Rows.Add(new[] { "<strong>" + StatsHover.Esc(L("window-total")) + "</strong>", "", "", "<strong>" + StatsHover.Text(Kcal(d, total)) + "</strong>" });
                table.Tips.Add(new[] { null, null, null, StatsHover.Text(L("window-total-tip", Kcal(d, total))) });
                blocks.Add(table);
                if (!DigestionPool.DiminishingReturns) blocks.Add(new TextBlock(StatsHover.Text(L("window-macros-note-off")), true));
                blocks.Add(new TextBlock(FiberBoost(d), true, FiberBoostTip(d)));
            }

            // Every slow nutrient running low, as chips with their best foods in the hover (v0.3.6i).
            blocks.Add(new HeadingBlock(L("window-heading-low")));
            var low = RunningLow(d).Where(n => !n.IsCalories).ToList();
            if (low.Count == 0)
            {
                blocks.Add(new TextBlock(StatsHover.Text(L("window-low-none"))));
            }
            else
            {
                var chips = new ChipsBlock();
                foreach (var n in low)
                {
                    float r = d.State.GetReserve(n.Code);
                    chips.Chips.Add((StatsHover.Esc(Lang.Get(n.LangKey)) + " " + DaysVtml(d, n, r), NutrientTip(d, n)));
                }
                blocks.Add(chips);
                blocks.Add(new TextBlock(Grey(L("window-low-hover")), true));
            }
            return blocks;
        }

        /// <summary>A macro's 24-hour calories, red when diminishing returns took some.</summary>
        private static string KcalCell(NutritionPageData d, float kcal, float lost) => lost > 0f
            ? "<font color=\"" + UnitColors.Rate + "\">" + StatsHover.Esc(Kcal(d, kcal)) + "</font>"
            : StatsHover.Text(Kcal(d, kcal));

        /// <summary>The hover of a macro's 24-hour calories: what they are, and what diminishing returns took with a tip.</summary>
        private static string KcalTip(NutritionPageData d, string name, float kcal, float lost)
        {
            string tip = StatsHover.Text(L("window-macro-kcal-tip", name, Kcal(d, kcal)));
            if (lost > 0f)
            {
                tip += "<br>" + StatsHover.Paint(L("window-macro-lost", Kcal(d, lost)), UnitColors.Rate);
                tip += "<br><br>" + Grey(L("window-macro-dr-tip"));
            }
            return tip;
        }

        /// <summary>A vitamin or mineral's hover: its name and what it does, then the best foods the player knows for it.</summary>
        private static string NutrientTip(NutritionPageData d, NutrientDefinition n, string middle = null) =>
            NutrientTitle(n) + (middle == null ? "" : "<br>" + middle) + "<br>" + BestFoods(d, n);

        /// <summary>"<b>Name</b>: what it does", in one line: the macros' short "-brief" text (v0.3.6l), else the role.</summary>
        private static string NutrientTitle(NutrientDefinition n)
        {
            string brief = "complexnutrition:nutrient-" + n.Code + "-brief";
            string role = Lang.HasTranslation(brief) ? Lang.Get(brief) : Lang.Get("complexnutrition:nutrient-" + n.Code + "-role");
            return "<strong>" + StatsHover.Esc(Lang.Get(n.LangKey)) + "</strong>: " + StatsHover.Text(role);
        }

        /// <summary>The hover of fiber's speed-up: the extra calories an hour it adds to each gut pool, and where it is fullest.</summary>
        private static string FiberBoostTip(NutritionPageData d)
        {
            var by = Digestion.FiberKcalPerHourByPool(d.Digest, d.State, d.Nutrients);
            var lines = new List<string>();
            if (by.Count > 0) lines.Add(StatsHover.Text(L("fiber-boost-tip")));
            foreach (var code in new[] { NutrientCodes.Carbohydrate, NutrientCodes.Fat, NutrientCodes.Protein })
            {
                if (!by.TryGetValue(code, out float kcal)) continue;
                var n = d.Nutrients.Get(code);
                if (n == null) continue;
                lines.Add("• " + StatsHover.Text(Lang.Get(n.LangKey) + ": ") + StatsHover.Paint(Kcal(d, kcal), UnitColors.Rate));
            }
            var fiber = d.Nutrients.Get(NutrientCodes.Fiber);
            if (fiber != null && d.Digest != null)
            {
                lines.Add((lines.Count > 0 ? "<br>" : "") + StatsHover.Text(L("window-fiber-stored", fiber.Format(d.State.GetReserve(fiber.Code), d.ShowPercent))));
                lines.Add(Grey(L("window-fiber-best", fiber.Format(d.Digest.FiberBonusFullAt, d.ShowPercent))));
            }
            return string.Join("<br>", lines);
        }

        // ---- Digestion ----

        public static List<PageBlock> DigestionPage(NutritionPageData d)
        {
            if (d.State == null) return Off();
            // The same hint as the Vitamins and Minerals tabs (v0.3.6l): each macro's bar hover says what it does.
            var blocks = new List<PageBlock> { new TextBlock(Grey(L("window-rows-hover")), true), new GapBlock(2) };
            var s = d.Digest;

            // v0.3.6i: a heading per bar, each with one short grey line under it; the rest is in the hovers.
            blocks.Add(new HeadingBlock(L("stats-fullness")));
            var full = FullnessBar(d);
            full.Label = StatsHover.Esc(L("window-now"));
            full.RightTip = StatsHover.Text(L("window-fullness-trend", Num(s?.FullnessDrainPerHour ?? 100f)));
            blocks.Add(full);
            if (s != null)
            {
                float hours = s.FullnessDrainPerHour > 0 ? d.Fullness / s.FullnessDrainPerHour : 0f;
                blocks.Add(new TextBlock(Grey(d.Fullness < 1f
                    ? L("window-stomach-empty-short")
                    : L("window-stomach-left", hours.ToString(hours < 10 ? "0.0" : "0", CultureInfo.InvariantCulture))), true));
            }
            if (s == null) return blocks;

            foreach (var code in new[] { NutrientCodes.Carbohydrate, NutrientCodes.Fat, NutrientCodes.Protein })
            {
                var pool = s.Pool(code);
                var n = d.Nutrients.Get(code);
                if (pool == null || n == null) continue;
                float reserve = d.State.GetReserve(code);
                float grams = reserve / 100f * n.DailyTarget;
                float window = Digestion.WindowGrams(d.State, code, pool);
                bool digesting = grams > DigestionPool.EmptyGrams;
                float kcal = digesting ? pool.Effective(window, grams) * pool.KcalPerGram : 0f;

                bool dr = DigestionPool.DiminishingReturns;
                // The limits only: what was digested lately is on the History tab (Miles, v0.3.6l).
                string limits = code == NutrientCodes.Protein
                    ? (dr ? L("window-gut-window-protein", Amt(d, code, s.ProteinNeedGrams), Amt(d, code, s.ProteinMaxGrams))
                          : L("window-gut-window-protein-off", Amt(d, code, s.ProteinNeedGrams)))
                    : (dr ? L("window-gut-window", pool.WindowHours, Amt(d, code, pool.DiminishingAboveGrams)) : null);

                float rate = digesting ? pool.Rate(grams, Digestion.FiberSpeed(s, d.State, pool)) : 0f;
                AddGut(blocks, d, n, grams, pool.MaxGrams, kcal, limits, rate, digesting ? pool.Effective(window, rate) * pool.KcalPerGram : 0f);
            }

            var fiber = d.Nutrients.Get(NutrientCodes.Fiber);
            if (fiber != null)
            {
                // Fiber's gut goes through the same AddGut as the macros (v0.3.6l); its reserve is in the boost line's hover.
                float fg = d.State.FiberGut;
                float frate = fg > DigestionPool.EmptyGrams ? Math.Min(fg, DigestionSettings.FiberGutRate(fiber)) : 0f;
                AddGut(blocks, d, fiber, fg, s.FiberGutMaxGrams, fg * s.FiberKcalPerGram, null, frate, frate * s.FiberKcalPerGram);
                blocks.Add(new TextBlock(FiberBoost(d), true, FiberBoostTip(d)));
            }
            return blocks;
        }

        /// <summary>
        /// One macro's gut on the Digestion tab: heading, a Digesting bar (grams to calories, its hover naming the numbers), and a
        /// grey line with the time left and the rate. Carbs, fats, protein and fiber all use it, so they always look the same.
        /// </summary>
        private static void AddGut(List<PageBlock> blocks, NutritionPageData d, NutrientDefinition n, float grams, float max, float kcal,
            string limits, float rate, float kcalRate)
        {
            string code = n.Code;
            bool digesting = grams > DigestionPool.EmptyGrams;
            blocks.Add(new HeadingBlock(Lang.Get(n.LangKey)));
            blocks.Add(new BarBlock
            {
                Label = StatsHover.Esc(L("window-digesting")),
                Trend = TrendOf(d, n),
                Value = grams,
                Max = Math.Max(max, grams),
                LineInterval = n.DailyTarget,
                Color = MacroColor(n),
                Right = digesting
                    ? StatsHover.Text(L("window-gut-to-kcal", Amt(d, code, grams), Kcal(d, kcal)))
                    : StatsHover.Text(Amt(d, code, grams)),
                Tip = NutrientTip(d, n, StatsHover.Text(L("window-gut-value", Amt(d, code, grams), Amt(d, code, max))) + (limits == null ? "" : "<br>" + StatsHover.Text(limits))),
            });
            blocks.Add(new TextBlock(digesting
                ? StatsHover.Vtml("complexnutrition:window-gut-left", Grey(HoursLeft(d.State.GetGutHours(code))),
                    StatsHover.Paint(L("gut-rate", Amt(d, code, rate), Kcal(d, kcalRate)), UnitColors.Rate))
                : Grey(L("window-gut-empty")), true));
        }

        /// <summary>"Empty in about 2.6 hours", from a gut pool's hours to empty.</summary>
        private static string HoursLeft(float hours) =>
            L("window-hours-left", hours < 10f ? hours.ToString("0.0", CultureInfo.InvariantCulture) : Math.Round(hours).ToString("0", CultureInfo.InvariantCulture));

        // ---- Vitamins and minerals ----

        public static List<PageBlock> Vitamins(NutritionPageData d) =>
            Group(d, n => n.Group == "WaterSolubleVitamin" || n.Group == "FatSolubleVitamin", "window-intro-vitamins");

        public static List<PageBlock> Minerals(NutritionPageData d) =>
            Group(d, n => n.Group == "Mineral", "window-intro-minerals");

        private static List<PageBlock> Group(NutritionPageData d, Func<NutrientDefinition, bool> which, string introKey)
        {
            if (d.State == null) return Off();
            // v0.3.6i: one row each, so the tab fits on one screen; what it does and the best foods are in the row's hover.
            var blocks = new List<PageBlock> { new TextBlock(Grey(L("window-rows-hover")), true), new GapBlock(2) };
            foreach (var n in d.Nutrients.All.Where(which))
            {
                var bar = ReserveBar(d, n);
                bar.Trend = TrendOf(d, n);
                bar.Tip = NutrientTip(d, n);
                blocks.Add(bar);
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

        public static readonly double[] GainedColor = { 0.45, 0.78, 0.40, 1.0 };
        public static readonly double[] BurnedColor = { 0.93, 0.42, 0.37, 1.0 };

        /// <summary>The top of both History charts unless a day went past it, in kcal.</summary>
        public const float ChartKcal = 5000f;

        /// <summary>The colour of each chart's top value, in its top left corner.</summary>
        public const string TopValueColor = "#ffd94a";

        private static string TopValue(string plain) => "<font color=\"" + TopValueColor + "\">" + StatsHover.Esc(plain) + "</font>";

        private static string Bold(string plain) => "<strong>" + StatsHover.Esc(plain) + "</strong>";

        /// <summary>"Today", "Yesterday" or "N days ago".</summary>
        private static string DayName(int daysAgo) => daysAgo <= 0 ? L("history-today") : daysAgo == 1 ? L("history-yesterday") : L("history-days-ago", daysAgo);

        /// <summary>
        /// The dashed line: the calories burned a day on average over the recorded days (up to 28), leaving out today while it is
        /// still under way. Until something has been burned, a day's need. Returns the amount and its "i" hover.
        /// </summary>
        public static (float mark, string tip) BurnMark(NutritionPageData d, HistoryData h, NutrientDefinition cal)
        {
            var full = h.Days.Take(Math.Max(0, h.Days.Count - 1)).Where(day => day.Burned > 0).ToList();
            if (full.Count == 0) full = h.Days.Where(day => day.Burned > 0).ToList();
            if (full.Count == 0)
                return (cal.DailyTarget, StatsHover.Text(L("history-mark-need", Kcal(d, cal.DailyTarget))));
            float avg = full.Average(day => day.Burned);
            return (avg, StatsHover.Text(full.Count == 1 ? L("history-mark-burn-one", Kcal(d, avg)) : L("history-mark-burn", Kcal(d, avg), full.Count)));
        }

        /// <summary>A food chart day's hover: the calories eaten in all and by food type.</summary>
        private static string FoodTip(NutritionPageData d, float[] stack, int daysAgo)
        {
            var sb = new StringBuilder(Bold(DayName(daysAgo)));
            float total = stack.Sum();
            sb.Append("<br>").Append(StatsHover.Text(total > 0 ? L("history-tip-ate", Kcal(d, total)) : L("history-tip-ate-none")));
            for (int i = 0; i < Categories.Length; i++)
            {
                if (stack[i] <= 0) continue;
                string pct = Math.Round(stack[i] / total * 100f).ToString("0", CultureInfo.InvariantCulture);
                sb.Append("<br>").Append("<font color=\"").Append(StatsHover.Hex(CategoryColors[Categories[i]], 0)).Append("\">\u25a0</font> ")
                  .Append(StatsHover.Text(L("history-tip-category", L("history-cat-" + Categories[i].ToLowerInvariant()), Kcal(d, stack[i]), pct)));
            }
            return sb.ToString();
        }

        /// <summary>The History tab: the last weeks of calories gained and burned (lines), and of calories eaten (bars, by food category).</summary>
        public static List<PageBlock> History(NutritionPageData d)
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

            var cal = d.Nutrients.Get(NutrientCodes.Calories);
            if (cal == null) return blocks;

            var stacks = h.Days.Select(Stack).ToArray();

            // Calories into and out of the reserve.
            var gained = h.Days.Select(day => day.Gained > 0 || day.Burned > 0 ? (float?)day.Gained : null).ToArray();
            var burned = h.Days.Select(day => day.Gained > 0 || day.Burned > 0 ? (float?)day.Burned : null).ToArray();
            // Both charts share one fixed scale (v0.3.5): 5,000kcal. A day past it reaches the top, and its hover gives the number.
            float flowMax = ChartKcal, barMax = ChartKcal;
            // The dashed line on both charts: the calories burned a day on average (v0.3.5), a day's need until anything was burned.
            var (mark, markTip) = BurnMark(d, h, cal);

            blocks.Add(new HeadingBlock(L("history-heading-flow")));
            blocks.Add(new GapBlock(2));
            blocks.Add(new ChartBlock
            {
                BarMax = flowMax,
                BarMark = mark,
                MarkTip = markTip,
                Line = gained,
                LineMax = flowMax,
                LineColor = GainedColor,
                ExtraLines = { (burned, BurnedColor) },
                TopLeft = TopValue(Kcal(d, flowMax)),
                Tips = h.Days.Select((day, i) => gained[i] == null ? null
                    : Bold(DayName(h.Days.Count - 1 - i)) + "<br>" + StatsHover.Text(L("history-tip-gained", Kcal(d, day.Gained))) + "<br>" + StatsHover.Text(L("history-tip-burned", Kcal(d, day.Burned)))).ToArray(),
                BottomLeft = StatsHover.Text(L("history-days-ago", h.Days.Count - 1)),
                BottomRight = StatsHover.Text(L("history-today")),
                Legend = { (GainedColor, StatsHover.Esc(L("history-gained"))), (BurnedColor, StatsHover.Esc(L("history-burned"))) },
            });

            // The last week in words.
            var week = h.Days.Skip(Math.Max(0, h.Days.Count - 8)).Take(Math.Min(7, Math.Max(0, h.Days.Count - 1))).ToList();
            var flowWeek = week.Where(day => day.Gained > 0 || day.Burned > 0).ToList();
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
                string pct = Math.Round(share).ToString("0", CultureInfo.InvariantCulture);
                string from = L("history-from-" + Categories[top].ToLowerInvariant());
                blocks.Add(new TextBlock(StatsHover.Text(flowWeek.Count > 0
                    ? L("history-week-gained-burned", Kcal(d, flowWeek.Average(day => day.Gained)), Kcal(d, flowWeek.Average(day => day.Burned)), week.Count, pct, from)
                    : L("history-week-eaten", Kcal(d, avg), week.Count, pct, from))));
            }

            blocks.Add(new HeadingBlock(L("history-heading-food")));
            blocks.Add(new GapBlock(2));

            var chart = new ChartBlock
            {
                Bars = stacks,
                SeriesColors = Categories.Select(c => CategoryColors[c]).ToArray(),
                BarMax = barMax,
                BarMark = mark,
                MarkTip = markTip,
                TopLeft = TopValue(Kcal(d, barMax)),
                Tips = h.Days.Select((day, i) => FoodTip(d, stacks[i], h.Days.Count - 1 - i)).ToArray(),
                BottomLeft = StatsHover.Text(L("history-days-ago", h.Days.Count - 1)),
                BottomRight = StatsHover.Text(L("history-today")),
            };
            for (int i = 0; i < Categories.Length; i++)
            {
                bool any = stacks.Any(st => st[i] > 0);
                if (Categories[i] == "Unknown" && !any) continue;
                chart.Legend.Add((CategoryColors[Categories[i]], StatsHover.Esc(L("history-cat-" + Categories[i].ToLowerInvariant()))));
            }
            blocks.Add(chart);
            return blocks;
        }
    }
}
