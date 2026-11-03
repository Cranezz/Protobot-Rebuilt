using System.Linq;
using System.Text.RegularExpressions;
using Parts_List;
using UnityEngine;

namespace Protobot.Interface {
    // V2: short, friendly part names for messages ("C-Channel", not "1x2x1 C-Channel (15)"): the user knows
    // which part they picked, so sizes, hole counts and materials are left out.
    public static class PartLabels {
        private static readonly string[] Materials = { "Aluminum", "Aluminium", "Steel", "Nylon", "Plastic", "Teflon", "Acetal" };

        public static string Short(GameObject part) {
            if (part == null) return "part";
            if (part.TryGetComponent(out SavedObject saved) && !string.IsNullOrEmpty(saved.id)) {
                var custom = Custom.CustomParts.Find(saved.id);
                if (custom != null) return custom.name;
                // The original's parts: the part type's own name (C-Channel, Screw, Shaft...).
                if (!saved.id.StartsWith("VX")) {
                    var type = PartsManager.GetPartType(saved.id);
                    if (type != null) return Singular(type.name);
                }
            }
            var name = part.TryGetComponent(out PartName named) && !string.IsNullOrEmpty(named.name) ? named.name : part.name;
            return Short(name);
        }

        /// <summary>A part's full name without sizes, counts, materials and notes in brackets.</summary>
        public static string Short(string name) {
            var text = Regex.Replace(name, @"\([^)]*\)", " ");
            var words = text.Split(new[] { ' ', ',' }, System.StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !Regex.IsMatch(w, @"\d") && !w.Contains("\"") && !Materials.Contains(w))
                .ToArray();
            var result = string.Join(" ", words).Trim();
            return result.Length > 0 ? result : name.Trim();
        }

        private static string Singular(string name) =>
            name.EndsWith("s") && !name.EndsWith("ss") && name != "Mechanisms" ? name.Substring(0, name.Length - 1) : name;
    }
}
