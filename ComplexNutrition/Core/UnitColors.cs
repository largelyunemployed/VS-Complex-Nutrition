using System.Text.RegularExpressions;

namespace ComplexNutrition.Core
{
    /// <summary>
    /// One colour per unit, used wherever the mod shows amounts in VTML (Stats panel and HUD hovers, food
    /// tooltips): grams green, calories orange, milligrams, micrograms and % DV yellow. The absorption rate segment of
    /// the advanced gut line is red, with its amounts still in their unit colours.
    /// </summary>
    public static class UnitColors
    {
        public const string Grams = "#8fd46a";
        public const string Kcal = "#f5a142";
        public const string Micro = "#f2d230";
        public const string Rate = "#ec6b5e";

        // A number with its unit right after it: "94g", "1,920kcal", "7.0mg", "4mcg". Not part of a longer word.
        private static readonly Regex Amount = new(@"(?<![\w.#])\d[\d,]*(?:\.\d+)?(kcal|mcg|mg|g)(?!\w)", RegexOptions.Compiled);
        // A daily-value amount, used for nutrients with no unit to show (the B vitamins): "2.1% DV". Yellow, like mg and mcg.
        private static readonly Regex DailyValue = new(@"(?<![\w.#])\d[\d,]*(?:\.\d+)?% DV\b", RegexOptions.Compiled);
        // A unit named on its own in brackets, as in "4kcal per gram (g)".
        private static readonly Regex BareUnit = new(@"(?<=\()(kcal|mcg|mg|g)(?=\))", RegexOptions.Compiled);

        private static string Let(this string s, System.Func<string, string> f) => f(s);

        /// <summary>The colour for a unit, or null for one we don't colour.</summary>
        public static string For(string unit) => unit switch
        {
            "g" => Grams,
            "kcal" => Kcal,
            "mg" or "mcg" => Micro,
            _ => null,
        };

        private static readonly Regex Tag = new(@"(<[^>]*>)", RegexOptions.Compiled);

        /// <summary>Like <see cref="Paint"/>, for text that already holds VTML tags: only the text between tags is painted.</summary>
        public static string PaintVtml(string vtml)
        {
            if (string.IsNullOrEmpty(vtml)) return vtml;
            var parts = Tag.Split(vtml);
            for (int i = 0; i < parts.Length; i++)
            {
                if (!parts[i].StartsWith("<")) parts[i] = Paint(parts[i]);
            }
            return string.Concat(parts);
        }

        /// <summary>Wraps every amount (and every unit named alone in brackets, like "(g)") in <paramref name="text"/> (plain or VTML-safe text, no tags) in its unit's colour.</summary>
        public static string Paint(string text) =>
            string.IsNullOrEmpty(text) ? text : BareUnit.Replace(
                Amount.Replace(text, m => "<font color=\"" + For(m.Groups[1].Value) + "\">" + m.Value + "</font>"),
                m => "<font color=\"" + For(m.Value) + "\">" + m.Value + "</font>")
                .Let(t => DailyValue.Replace(t, m => "<font color=\"" + Micro + "\">" + m.Value + "</font>"));
    }
}
