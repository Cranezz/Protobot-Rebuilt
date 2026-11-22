using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Protobot.Online {
    /// <summary>V2: keeps offensive words out of names other players see (player names, usernames, match names, robot
    /// names, leaderboard names). <see cref="Allowed"/> refuses one when it's typed; <see cref="Shown"/> hides one that
    /// came from someone else (a name made before the filter, or by a modified app). Letters are compared after undoing
    /// look-alike characters (0 = o, 1 = i, $ = s...), dropping separators and doubled letters, so "B.@.D" is caught too.
    /// Short words only count as a whole word, so ordinary names that happen to contain them ("Cockpit",
    /// "Assembly", "Grapefruit") stay allowed.</summary>
    public static class NameFilter {
        // ROT13, so the list doesn't read as a list of insults in the source. Long roots match anywhere in the name;
        // short ones only as a whole word.
        private static readonly string[] AnywhereRot = {
            "shpx", "fuvg", "ovgpu", "phag", "avttre", "avttn", "snttbg", "zbgureshpx", "juber", "fyhg", "ergneq", "qvpxurnq",
            "nffubyr", "chffl", "onfgneq", "jnaxre", "gjng", "pbpxfhpx", "qvyqb", "anmv", "uvgyre", "zbyrfg", "crqbcuvy", "xxx",
            "puvax", "ornare", "jrgonpx", "genaal", "xlxr", "cbea", "uragnv", "pyvg", "frkl", "ahqr", "anxrq", "betnfz",
            "znfgheong", "rwnphyng", "cravf", "intvan", "intvan", "gvggl", "fcrez", "pbaqbz", "ohgguby", "erpghz", "fuvgr", "ovngpu",
            "shpxre", "sntt",
        };
        private static readonly string[] WordRot = {
            "nff", "nffrf", "qvpx", "pbpx", "snt", "gvg", "gvgf", "pbba", "ubr", "ubrf", "wvmm", "phz",
            "frk", "encr", "encrq", "encvfg", "fcvp", "xvxr", "jnc", "cvff", "anqf", "obbo", "obbof", "nany",
        };
        private static readonly string[] Fine = { "shiitake", "scunthorpe", "penistone", "cockburn", "dickinson", "hancock", "peacock", "shuttlecock" };
        private static HashSet<string> words;
        private static string[] anywhere;

        // An entry without doubled letters is compared squashed ("fuuuck" is caught); one with them as its exact letters
        // (squashed, "nigger" would be "niger" and "boobs" "bobs": real names).
        private static void Load() {
            if (words != null) return;
            anywhere = AnywhereRot.Select(Rot13).ToArray();
            words = new HashSet<string>(WordRot.Select(Rot13));
        }

        private static bool Doubled(string s) {
            for (var i = 1; i < s.Length; i++) if (s[i] == s[i - 1]) return true;
            return false;
        }

        private static string Letters(string s) => new string(s.Select(Letter).Where(c => c != '\0').ToArray());

        private static bool IsWord(string part) {
            var letters = Letters(part);
            return words.Contains(letters) || words.Any(w => !Doubled(w) && Squash(letters) == w);
        }

        private static string Rot13(string s) => new string(s.Select(c => c >= 'a' && c <= 'z' ? (char)('a' + (c - 'a' + 13) % 26) : c).ToArray());

        // Look-alikes to letters, everything else that isn't a letter dropped, doubled letters made single.
        private static char Letter(char c) {
            c = char.ToLowerInvariant(c);
            switch (c) {
                case '0': return 'o';
                case '1': case '!': case '|': case 'í': case 'ì': return 'i';
                case '3': case 'é': case 'è': case 'ë': return 'e';
                case '4': case '@': case 'á': case 'à': case 'ä': return 'a';
                case '5': case '$': return 's';
                case '7': case '+': return 't';
                case '8': return 'b';
                case '9': return 'g';
                case 'ö': case 'ó': return 'o';
                case 'ü': case 'ú': return 'u';
                default: return c >= 'a' && c <= 'z' ? c : '\0';
            }
        }

        private static string Squash(string s) {
            var sb = new StringBuilder(s.Length);
            foreach (var raw in s) {
                var c = Letter(raw);
                if (c == '\0') continue;
                if (sb.Length > 0 && sb[sb.Length - 1] == c) continue;
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Whether a name is fine to show to other players.</summary>
        public static bool Allowed(string name) {
            if (string.IsNullOrWhiteSpace(name)) return true;
            Load();
            // Real words that happen to contain a listed one.
            foreach (var fine in Fine) name = System.Text.RegularExpressions.Regex.Replace(name, fine, " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var whole = Squash(name);
            var letters = Letters(name);
            foreach (var bad in anywhere)
                if (Doubled(bad) ? letters.Contains(bad) : whole.Contains(bad)) return false;
            // Words: split where a letter-like character is followed by a space or symbol ("Big Ass Bot", "ass_bot"),
            // and at lower-to-upper case changes ("AssBot").
            var parts = new List<string>();
            var current = new StringBuilder();
            char previous = ' ';
            foreach (var c in name) {
                var letter = Letter(c);
                var split = letter == '\0' || char.IsUpper(c) && char.IsLower(previous);
                if (split && current.Length > 0) {
                    parts.Add(current.ToString());
                    current.Clear();
                }
                if (letter != '\0') current.Append(c);
                previous = c;
            }
            if (current.Length > 0) parts.Add(current.ToString());
            foreach (var part in parts)
                if (IsWord(part)) return false;
            // A run of single letters spelling a word ("a s s").
            if (parts.Count > 1 && parts.All(p => p.Length == 1) && IsWord(name)) return false;
            return true;
        }

        /// <summary>A name to show: the name itself, or <paramref name="instead"/> when it isn't allowed.</summary>
        public static string Shown(string name, string instead = "Player") => Allowed(name) ? name : instead;
    }
}
