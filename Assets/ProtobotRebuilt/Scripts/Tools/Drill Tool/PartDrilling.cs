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
            var filter = part.GetComponent<MeshFilter>();
            // V2: the wall can be thicker than its hole collider (then the back face kept its square hole);
            // cut through the metal the mesh actually has around the hole.
            var (front, back) = WallExtent(filter.sharedMesh, hole.localPosition, normal, depth,
                Mathf.Max(diameter, Mathf.Max(hole.localScale.x, hole.localScale.y)) * 0.75f + 0.02f);
            const float margin = 0.002f;
            var info = new HoleInfo {
                found = true,
                center = hole.localPosition + normal * (front + margin),
                normal = normal,
                thickness = front - back + 2f * margin,
                holeRadius = diameter / 2f,
                round = true,
            };

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

        // How far the wall goes in front of and behind the hole's centre (along its axis), from the mesh's
        // vertices around the hole; at least the hole collider's own depth, at most a little more than it.
        private static (float front, float back) WallExtent(Mesh mesh, Vector3 center, Vector3 axis, float depth, float radius) {
            float front = depth / 2f, back = -depth / 2f;
            var reach = Mathf.Max(depth * 1.5f, 0.1f);
            foreach (var vertex in mesh.vertices) {
                var offset = vertex - center;
                var along = Vector3.Dot(offset, axis);
                if (Mathf.Abs(along) > reach || (offset - axis * along).sqrMagnitude > radius * radius)
                    continue;
                front = Mathf.Max(front, along);
                back = Mathf.Min(back, along);
            }
            return (front, back);
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
