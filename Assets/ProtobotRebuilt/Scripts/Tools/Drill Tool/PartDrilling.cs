using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.Geometry;
using Protobot.StateSystems;
using UnityEngine;

namespace Protobot.Tools {
    // V2: drills a part's square hole out round. The cut is made in the part's mesh (HoleDrilling), and the hole
    // becomes a round hole for snapping. Drilled holes are kept in the part's SavedObject.state ("drill:index,
    // diameter;"), which build files already store, and drilled again when a build is loaded.
    public static class PartDrilling {
        public const float BitDiameter = 0.25f; // inches
        private const string Prefix = "drill:";

        public static List<HoleCollider> Holes(GameObject part) =>
            part.GetComponentsInChildren<HoleCollider>(true).Where(h => h.transform.parent == part.transform).ToList();

        public static bool IsRound(HoleCollider hole) {
            var round = HoleShapes.instance.GetShapeMesh("circle");
            return round != null && hole.GetComponent<MeshCollider>().sharedMesh == round;
        }

        /// <summary>Whether the drill can cut this part (any part whose mesh can be edited: people drill most things).</summary>
        public static bool IsDrillable(GameObject part) => PartCutting.IsCuttable(part);

        /// <summary>Cuts hole <paramref name="index"/> of the part round. False if there was nothing to cut.</summary>
        public static bool Drill(GameObject part, int index, float diameter) {
            var holes = Holes(part);
            if (index < 0 || index >= holes.Count)
                return false;
            var hole = holes[index].transform;

            // The hole collider sits in the middle of the wall, its forward along the hole.
            var normal = (hole.localRotation * Vector3.forward).normalized;
            var depth = Mathf.Abs(hole.localScale.z);
            const float margin = 0.002f;
            var info = new HoleInfo {
                found = true,
                center = hole.localPosition + normal * (depth / 2f + margin),
                normal = normal,
                thickness = depth + 2f * margin,
                holeRadius = diameter / 2f,
                round = true,
            };

            var filter = part.GetComponent<MeshFilter>();
            var drilled = HoleDrilling.Drill(filter.sharedMesh, info, diameter / 2f);
            if (drilled == null)
                return false;

            filter.sharedMesh = drilled;
            if (part.TryGetComponent(out MeshCollider collider))
                collider.sharedMesh = drilled;

            var round = HoleShapes.instance.GetShapeMesh("circle");
            var holeCollider = hole.GetComponent<MeshCollider>();
            if (round != null)
                holeCollider.sharedMesh = round;
            hole.localScale = new Vector3(diameter, diameter, hole.localScale.z);
            var holeData = hole.GetComponent<HoleCollider>().holeData;
            holeData.shape = holeCollider.sharedMesh;
            holeData.size = new Vector2(diameter, diameter);
            return true;
        }

        public static void Record(SavedObject saved, int index, float diameter) {
            saved.state += Prefix + index + "," + diameter.ToString(CultureInfo.InvariantCulture) + ";";
        }

        /// <summary>
        /// Makes the edits listed in the part's saved state again (after loading a build): drilled holes and saw
        /// cuts, in the order they were made (a drilled hole's index counts the holes the part had then).
        /// </summary>
        public static void ApplySaved(GameObject part) {
            if (!part.TryGetComponent(out SavedObject saved) || string.IsNullOrEmpty(saved.state))
                return;
            foreach (var entry in saved.state.Split(';')) {
                if (PartCutting.TryParse(entry, out var point, out var normal)) {
                    if (PartCutting.Cut(part, point, normal) && part.TryGetComponent(out Parts_List.PartName partName)
                        && !partName.name.EndsWith("(cut)"))
                        partName.name += " (cut)";
                    continue;
                }
                if (!entry.StartsWith(Prefix))
                    continue;
                var values = entry.Substring(Prefix.Length).Split(',');
                if (values.Length == 2
                    && int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                    && float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var diameter))
                    Drill(part, index, diameter);
            }
        }
    }

    // Undo/redo of a drilled hole: puts back the part's mesh, its holes' shapes and its saved state.
    public class DrillElement : IElement {
        private readonly GameObject part;
        private readonly Mesh mesh;
        private readonly string state;
        private readonly List<(MeshCollider collider, Mesh shape, Vector3 scale)> holes = new();

        public DrillElement(GameObject part) {
            this.part = part;
            mesh = part.GetComponent<MeshFilter>().sharedMesh;
            state = part.GetComponent<SavedObject>().state;
            foreach (var hole in PartDrilling.Holes(part)) {
                var collider = hole.GetComponent<MeshCollider>();
                holes.Add((collider, collider.sharedMesh, hole.transform.localScale));
            }
        }

        public void Load() {
            if (part == null)
                return;
            part.GetComponent<MeshFilter>().sharedMesh = mesh;
            if (part.TryGetComponent(out MeshCollider collider))
                collider.sharedMesh = mesh;
            part.GetComponent<SavedObject>().state = state;
            foreach (var (holeCollider, shape, scale) in holes) {
                if (holeCollider == null)
                    continue;
                holeCollider.sharedMesh = shape;
                holeCollider.transform.localScale = scale;
                var data = holeCollider.GetComponent<HoleCollider>().holeData;
                data.shape = shape;
                data.size = new Vector2(scale.x, scale.y);
            }
        }
    }
}
