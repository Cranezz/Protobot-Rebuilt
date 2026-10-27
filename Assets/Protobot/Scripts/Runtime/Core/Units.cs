using System.Globalization;
using UnityEngine;

namespace Protobot.Core
{
    /// <summary>
    /// Lengths as the app shows them: inches (VEX parts are designed in inches). Exact sixteenths are written
    /// as fractions ("17 1/2 in", "9/16 in"), anything else as a decimal ("0.266 in"). The world is in metres.
    /// </summary>
    public static class Units
    {
        public const float MetersPerInch = 0.0254f;

        public static float ToInches(float meters) => meters / MetersPerInch;
        public static float FromInches(float inches) => inches * MetersPerInch;

        public static string Inches(float meters, bool suffix = true)
        {
            var inches = meters / MetersPerInch;
            var text = Fraction(inches) ?? inches.ToString(Mathf.Abs(inches) < 10f ? "0.###" : "0.##", CultureInfo.InvariantCulture);
            return suffix ? text + " in" : text;
        }

        /// <summary>"3 1/4" for a value within a hair of a sixteenth, otherwise null.</summary>
        public static string Fraction(float inches)
        {
            var sixteenths = Mathf.RoundToInt(inches * 16f);
            if (Mathf.Abs(inches * 16f - sixteenths) > 0.02f)
                return null;
            var sign = sixteenths < 0 ? "-" : "";
            sixteenths = Mathf.Abs(sixteenths);
            int whole = sixteenths / 16, numerator = sixteenths % 16, denominator = 16;
            if (numerator == 0)
                return sign + whole;
            while (numerator % 2 == 0)
            {
                numerator /= 2;
                denominator /= 2;
            }
            return sign + (whole > 0 ? $"{whole} {numerator}/{denominator}" : $"{numerator}/{denominator}");
        }

        /// <summary>Reads "3.5", "3 1/2", "3-1/2", "7/8", with or without "in" or a " mark.</summary>
        public static bool TryParseInches(string text, out float inches)
        {
            inches = 0f;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            var t = text.Trim().ToLowerInvariant().Replace("inches", "").Replace("in", "").Replace("\"", "").Trim();
            var negative = t.StartsWith("-");
            if (negative)
                t = t.Substring(1).Trim();
            t = t.Replace('-', ' ');
            var parts = t.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts.Length > 2)
                return false;
            var total = 0f;
            foreach (var part in parts)
            {
                var slash = part.IndexOf('/');
                if (slash > 0)
                {
                    if (!float.TryParse(part.Substring(0, slash), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
                        || !float.TryParse(part.Substring(slash + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || d == 0f)
                        return false;
                    total += n / d;
                }
                else if (float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    total += v;
                }
                else
                {
                    return false;
                }
            }
            inches = negative ? -total : total;
            return true;
        }
    }
}
