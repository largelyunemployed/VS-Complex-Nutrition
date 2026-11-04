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
        public bool ShowPercent;
        public IReadOnlyList<KnownFood> Known = new List<KnownFood>();
    }

    /// <summary>
    /// Builds the nutrition window's tabs as page blocks. v0.3.0 has Overview, Digestion, Vitamins and Minerals;
    /// History (v0.3.1) adds a fifth. Pure text and numbers, so previews can build them outside the game.
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
                : StatsHover.Vtml("complexnutrition:fiber-boost", StatsHover.Paint(L("kcal-rate", Kcal(d, kcal)), UnitColors.Rate));
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
            var blocks = new List<PageBlock> { new TextBlock(StatsHover.Text(L(introKey)), true), new GapBlock(2) };
            foreach (var n in d.Nutrients.All.Where(which))
            {
                blocks.Add(new HeadingBlock(Lang.Get(n.LangKey)));
                blocks.Add(ReserveBar(d, n, L("window-stored")));
                blocks.Add(new TextBlock(StatsHover.Text(Lang.Get("complexnutrition:nutrient-" + n.Code + "-role")), true));
                blocks.Add(new TextBlock(BestFoods(d, n), true));
            }
            return blocks;
        }
    }
}
