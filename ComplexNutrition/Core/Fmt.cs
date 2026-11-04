using System;
using System.Globalization;

namespace ComplexNutrition.Core
{
    /// <summary>Numbers as the mod shows them everywhere: whole numbers from 10 up ("1,920"), one decimal below ("7.5", "0.4").</summary>
    public static class Fmt
    {
        public static string Number(float v) => Math.Abs(v) >= 10f
            ? Math.Round(v).ToString("#,0", CultureInfo.InvariantCulture)
            : v.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
