using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Protobot.Motion {
    // V2: colors made to match the real parts and their family (VEX-CAD's models came in with odd ones), for the
    // parts in the build and their pictures in the parts list:
    // - flex wheels by hardness, as VEX sells them: 30A light gray, 40A dark gray, 60A black;
    // - every spacer gray (the user's choice: one color for all; paint them any other);
    // - couplers and shoulder screws steel, like the screws.
    // Colors are about half their real value (the app's light doubles them: CLAUDE.md "Colors").
    public static class PartColors {
        private static readonly Dictionary<string, Material> made = new();

        public static Material[] For(string id, Material[] current) {
            if (id == null || current == null || current.Length == 0) return null;
            var name = id.IndexOf('-') > 0 ? id.Substring(id.IndexOf('-') + 1) : id;
            if ((id.StartsWith("VXWHEELS") && name.Contains("Flex Wheel")) || id.StartsWith("FWHL-")) {
                if (id.StartsWith("FWHL-")) name = "Flex Wheel, 40A";   // the original's flex wheels: the medium one
                var color = name.Contains("30A") ? new Color(0.36f, 0.36f, 0.36f) : name.Contains("40A") ? new Color(0.16f, 0.16f, 0.17f) : name.Contains("60A") ? new Color(0.05f, 0.05f, 0.05f) : (Color?)null;
                if (color == null) return null;
                var m = Tinted("Flex " + name.Substring(name.IndexOf(',') + 1, 4), current[0], color.Value, 0.15f);
                return Enumerable.Repeat(m, current.Length).ToArray();
            }
            // Every spacer one gray (nylon, high strength, plastic, click-on; VEX's and the original's): paint them
            // any other color.
            if ((id.StartsWith("VXSPACERS") && name.Contains("Spacer")) || id.StartsWith("SPCR-") || id.StartsWith("PSPC-")) {
                var m = Tinted("Spacer Gray", current[0], new Color(0.30f, 0.30f, 0.31f), 0.25f);
                return Enumerable.Repeat(m, current.Length).ToArray();
            }
            if (id.StartsWith("VXHARDWARE") && (name.Contains("Coupler") || name.Contains("Shoulder Screw")))
                return Like("SCRW", null, current.Length);
            if (id.StartsWith("VXSLIDEASSEMBLYP") && name.Contains("Slide Member"))
                return Like("SCRW", null, current.Length);
            if (id.StartsWith("VXPNEUMATICS")) return Pneumatic(name, current);
            return null;
        }

        // V2: V5 pneumatics as VEX makes them (the V5 Pneumatics Kit guide's photos): push-in fittings, the plug and
        // the shut off valve black with red release collars and nickel (silver) threads; the double acting solenoid
        // black; the regulator's body cream. VEX-CAD's models had them green, white and blue.
        private static readonly string[] BlackFittings = { "Elbow Fitting", "Air Flow Valve Fitting", "Tee Fittings", "Pneumatic Hand Valve", "Pneumatic Fitting Plug" };

        private static Material[] Pneumatic(string name, Material[] current) {
            Material Named(string material) => Library(material);
            System.Func<string, Material> map = null;
            if (BlackFittings.Contains(name))
                map = m => m.Contains("Green") ? Named("Black Plastic")
                    : m.Contains("White") ? (name == "Pneumatic Hand Valve" || name == "Pneumatic Fitting Plug" ? Named("Black Plastic") : Named("Silver Steel"))
                    : null;
            else if (name == "Straight Pneumatic Fitting")
                map = m => m.StartsWith("Gray Plastic") ? Named("Silver Steel") : null;
            else if (name.StartsWith("Solenoid") && name.Contains("Double"))
                map = m => m.Contains("Blue") || m.Contains("Green") ? Named("Black Plastic") : m.Contains("White") ? Named("Dark Gray Plastic") : null;
            else if (name == "Pressure Regulator")
                map = m => m.StartsWith("Gray Plastic") ? Tinted("Regulator Cream", current[0], new Color(0.47f, 0.44f, 0.36f), 0.2f) : null;
            if (map == null) return null;
            var result = new Material[current.Length];
            var changed = false;
            for (var i = 0; i < current.Length; i++) {
                var to = current[i] != null ? map(current[i].name) : null;
                result[i] = to != null ? to : current[i];
                changed |= to != null;
            }
            return changed ? result : null;
        }

        private static Dictionary<string, Material> library;

        // A VEX-CAD part material by name (from the pneumatics family's models).
        private static Material Library(string name) {
            if (library == null) {
                library = new Dictionary<string, Material>();
                var type = PartsManager.GetPartType("VXPNEUMATICS");
                if (type != null)
                    foreach (var r in type.GetComponentsInChildren<MeshRenderer>(true))
                        foreach (var m in r.sharedMaterials)
                            if (m != null && !library.ContainsKey(m.name)) library[m.name] = m;
            }
            return library.TryGetValue(name, out var found) ? found : null;
        }

        public static void Apply(GameObject part, string id) {
            if (part == null || !part.TryGetComponent(out MeshRenderer renderer)) return;
            var materials = For(id, renderer.sharedMaterials);
            if (materials != null) renderer.sharedMaterials = materials;
        }

        // The first material of an original part type (its first child with that first parameter).
        private static Material[] Like(string typeId, string p1, int count) {
            var key = "like|" + typeId + "|" + p1;
            if (!made.TryGetValue(key, out var material) || material == null) {
                material = null;
                var type = PartsManager.GetPartType(typeId);
                if (type != null)
                    foreach (var data in type.GetComponentsInChildren<PartData>(true))
                        if ((p1 == null || data.param1Value == p1) && data.TryGetComponent(out MeshRenderer r) && r.sharedMaterial != null) {
                            material = r.sharedMaterial;
                            break;
                        }
                made[key] = material;
            }
            return material != null ? Enumerable.Repeat(material, count).ToArray() : null;
        }

        private static Material Tinted(string key, Material basis, Color color, float smoothness) {
            if (made.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(basis) { name = key };
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            return made[key] = m;
        }
    }
}
