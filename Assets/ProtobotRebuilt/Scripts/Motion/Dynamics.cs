using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Protobot.Motion {
    // V2: parts that move. Every generated part passes through Attach (PartGenerator.SetId): pneumatic cylinders
    // get a sliding rod, motors a drive. Shared here: what's fastened to what (the screws' groups), and the
    // tokens a moving part keeps in its saved state ("piston:1;", "cartridge:600;").
    public static class Dynamics {
        public static void Attach(GameObject part, string id) {
            if (part == null || string.IsNullOrEmpty(id)) return;
            if (id.StartsWith("PNMT-") && part.GetComponent<PneumaticCylinder>() == null)
                part.AddComponent<PneumaticCylinder>();
            else if (id.StartsWith("MOTR-") && part.GetComponent<MotorDrive>() == null)
                part.AddComponent<MotorDrive>();
        }

        /// <summary>
        /// Everything fastened to the start objects through screws (and a cylinder's rod end), not going through
        /// any of the stop objects. frame is true when a stop object (the cylinder or motor itself) was reached
        /// another way than from link (a cylinder's rod end, which is fastened to the cylinder by design): what
        /// should move is fastened to what holds it still.
        /// </summary>
        public static HashSet<GameObject> Fastened(IEnumerable<GameObject> start, ICollection<GameObject> stop, System.Func<GameObject, bool> skip, out bool frame, GameObject link = null) {
            var found = new HashSet<GameObject>();
            var queue = new Queue<GameObject>();
            frame = false;
            foreach (var s in start)
                if (s != null && found.Add(s)) queue.Enqueue(s);
            while (queue.Count > 0) {
                var obj = queue.Dequeue();
                foreach (var next in GroupManager.Neighbours(obj)) {
                    if (next == null || next.IsDeleted() || found.Contains(next)) continue;
                    if (!Linkage.Fastens(obj, next)) continue;  // a joint: what's on its other side turns

                    if (stop.Contains(next)) {
                        if (obj != link) frame = true;
                        continue;
                    }
                    if (skip != null && skip(next)) continue;
                    found.Add(next);
                    queue.Enqueue(next);
                }
            }
            return found;
        }

        // ------------------------------------------------------------------ saved state tokens

        public static string Token(GameObject part, string key) {
            if (part == null || !part.TryGetComponent(out SavedObject saved) || string.IsNullOrEmpty(saved.state)) return null;
            foreach (var entry in saved.state.Split(';'))
                if (entry.StartsWith(key + ":")) return entry.Substring(key.Length + 1);
            return null;
        }

        public static void SetToken(GameObject part, string key, string value) {
            if (part == null || !part.TryGetComponent(out SavedObject saved)) return;
            var kept = new List<string>();
            foreach (var entry in (saved.state ?? "").Split(';'))
                if (entry.Length > 0 && !entry.StartsWith(key + ":")) kept.Add(entry);
            if (value != null) kept.Add(key + ":" + value);
            saved.state = kept.Count > 0 ? string.Join(";", kept) + ";" : "";
        }

        public static string Inches(float value) => value.ToString("0.###", CultureInfo.InvariantCulture) + "\"";

        // ------------------------------------------------------------------ a highlight like a hole's

        private static Material highlight;

        /// <summary>The see-through blue the hovered hole's face is drawn in (taken from it), for thread marks.</summary>
        public static Material Highlight {
            get {
                if (highlight != null) return highlight;
                foreach (var face in Object.FindObjectsByType<HoleFace>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (face.TryGetComponent(out MeshRenderer renderer) && renderer.sharedMaterial != null) {
                        highlight = new Material(renderer.sharedMaterial);
                        break;
                    }
                if (highlight == null) {
                    highlight = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                    highlight.color = Interface.Theme.Accent;
                }
                return highlight;
            }
        }
    }
}
