using System.Globalization;
using UnityEngine;

namespace Protobot.Interface {
    // V2: lengths as the interface writes them (the world is in inches): exact 64ths as fractions (3 1/2",
    // 13/64"), anything else to three decimals (2.718").
    public static class Inch {
        public static string Format(float inches, bool mark = true) {
            var suffix = mark ? "\"" : "";
            var sign = inches < 0f ? "-" : "";
            inches = Mathf.Abs(inches);
            var sixtyFourths = Mathf.RoundToInt(inches * 64f);
            if (Mathf.Abs(inches * 64f - sixtyFourths) < 0.01f) {
                int whole = sixtyFourths / 64, numerator = sixtyFourths % 64, denominator = 64;
                if (numerator == 0)
                    return sign + whole + suffix;
                while (numerator % 2 == 0) {
                    numerator /= 2;
                    denominator /= 2;
                }
                return sign + (whole > 0 ? $"{whole} {numerator}/{denominator}" : $"{numerator}/{denominator}") + suffix;
            }
            return sign + inches.ToString("0.###", CultureInfo.InvariantCulture) + suffix;
        }

        /// <summary>Reads "3.5", "3 1/2", "7/8", "13/64\"" (Units' parser, in inches).</summary>
        public static bool TryParse(string text, out float inches) => Core.Units.TryParseInches(text, out inches);
    }
}
