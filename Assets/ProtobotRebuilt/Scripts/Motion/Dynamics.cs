using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
            else if (SlideTruck.IsTruck(id) && part.GetComponent<SlideTruck>() == null)
                part.AddComponent<SlideTruck>();   // V2: rides in a slide member
            else if (UniversalJoint.Is(id) && part.GetComponent<UniversalJoint>() == null)
                part.AddComponent<UniversalJoint>();   // V2: bends at its cross
            else if (HingePart.IsHinge(id) && part.GetComponent<HingePart>() == null)
                part.AddComponent<HingePart>();   // V2: one part, both leaves and the pin
            if (id.StartsWith("VX")) {
                PartColors.Apply(part, id);   // V2: real colors (flex wheels by hardness, spacers, steel)
                CleanVexHoles(part, id);
                VexHoleFixes.Apply(part, id);   // V2: long slots, wrong holes
                if (id.Contains("License Plate") && part.GetComponent<LicensePlate>() == null) part.AddComponent<LicensePlate>();   // V2: team number
                if (RubberLinkPart.Is(id) && part.GetComponent<RubberLinkPart>() == null) part.AddComponent<RubberLinkPart>();   // V2: studs, bending
            }
            else if (VexHoleFixes.Handles(id)) VexHoleFixes.Apply(part, id);
            if (id.StartsWith("MCMS-")) PartColors.Apply(part, id);   // V2: the original's gear kit in VEX's colors
            if (id.StartsWith("FWHL-") || id.StartsWith("SPCR-") || id.StartsWith("PSPC-")) PartColors.Apply(part, id);   // V2: flex wheels by hardness, gray spacers
        }

        // V2: VEX-CAD parts' holes came from the hole scanner, which also found the openings in wheels (between
        // spokes), gears and electronics as odd rectangular "holes". On anything that isn't structure (where slots are
        // real) only round and square holes up to 0.3" stay. A wheel's, gear's or sprocket's middle hole always stays,
        // and gets the original's inserts (brass on a screw, silver on a shaft).
        private static void CleanVexHoles(GameObject part, string id) {
            var type = PartsManager.GetPartType(id);
            if (type == null || type.group == PartType.PartGroup.Structure) return;
            if (id.Contains("Hex Retainer")) return;
            var followerBracket = id.Contains("Cam Follower Mounting Bracket");   // V2: its window holds the follower   // V2: VexHoleFixes.Retainer sorts it (the hex pocket is a nut seat)
            var holes = part.GetComponentsInChildren<HoleCollider>(true).Where(h => h.transform.parent == part.transform).ToList();
            if (holes.Count == 0) return;
            HoleCollider middle = null;
            if (part.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null) {
                var center = filter.sharedMesh.bounds.center;
                middle = holes.OrderBy(h => (h.transform.localPosition - center).sqrMagnitude).First();
                if ((middle.transform.localPosition - center).magnitude > 0.2f) middle = null;
            }
            foreach (var hole in holes) {
                if (hole == middle) continue;
                var scale = hole.transform.localScale;
                var wide = Mathf.Max(scale.x, scale.y);
                var narrow = Mathf.Min(scale.x, scale.y);
                var odd = hole.holeData != null && hole.holeData.outline != null;
                if (followerBracket && narrow > 0.4f) continue;
                if (wide > 0.3f || wide - narrow > 0.03f || odd) {
                    hole.transform.SetParent(null);
                    Object.DestroyImmediate(hole.gameObject);
                }
            }
            if (middle != null && GearTrain.TurnsWithShaft(part) && middle.GetComponent<HoleInsert>() == null)
                middle.gameObject.AddComponent<HoleInsert>();
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

        // ------------------------------------------------------------------ build version

        private static int stateChanges;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitVersion() {
            stateChanges = 0;
            StateSystems.StateSystem.OnChangeState += () => stateChanges++;
        }

        /// <summary>Changes whenever the build does (an undo step, a part made, shown or removed): caches of it
        /// (gear trains, the sprockets chains go round) compare this to know when to look again.</summary>
        public static long Version => ((long)stateChanges << 32) | (uint)SavedObject.Changes;

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
