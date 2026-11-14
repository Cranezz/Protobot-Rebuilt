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

        // V2: a hole drilled across the part and then undone is kept switched off: it isn't counted.
        public static List<HoleCollider> Holes(GameObject part) =>
            part.GetComponentsInChildren<HoleCollider>(true).Where(h => h.transform.parent == part.transform
                && (h.gameObject.activeSelf || h.GetComponent<DrilledHole>() == null)).ToList();

        // ------------------------------------------------------------------ V2: holes across a standoff or HS shaft

        private const string CrossPrefix = "xdrill:";

        /// <summary>Parts a hole can be drilled across (through the side), for a screw: standoffs and HS shafts.</summary>
        public static bool CanCrossDrill(GameObject part) => PartBend.IsBendable(part) && !PartBend.IsLink(part);

        /// <summary>Where a hole across the part goes for a click on its side: on its middle line (1/16" steps from
        /// its end unless free), straight through along the face's normal. In the straight part's frame (its own
        /// space with its scale), with how wide the part is that way.</summary>
        public static bool CrossSpot(GameObject part, Vector3 point, Vector3 normal, float diameter, bool free,
                                     out Vector3 center, out Vector3 axis, out float width, out float along) {
            var bends = PartBend.SavedBends(part);
            var (axisIndex, low, length, middle) = PartBend.Frame(part, PartBend.Straight(part));
            along = PartBend.Along(part, point, bends);
            if (!free) along = Mathf.Round(along * 16f) / 16f;
            along = Mathf.Clamp(along, diameter / 2f + 0.03f, length - diameter / 2f - 0.03f);
            var (_, turn) = PartBend.CenterAt(part, along, bends);
            var local = Quaternion.Inverse(turn) * (Quaternion.Inverse(part.transform.rotation) * normal);
            local[axisIndex] = 0f;
            center = middle;
            center[axisIndex] = low + along;
            axis = local.sqrMagnitude > 1e-6f ? local.normalized : Vector3.zero;
            width = axis == Vector3.zero ? 0f : 2f * HalfWidth(part, center, axis, axisIndex);
            return axis != Vector3.zero && width > 0.02f && length > diameter + 0.06f;
        }

        // How far the part's material goes from its middle line along a way across it: a ray from outside onto the
        // straight mesh (a shaft's box has no points along its middle to measure by).
        private static GameObject probe;

        private static float HalfWidth(GameObject part, Vector3 center, Vector3 axis, int axisIndex) {
            var scale = part.transform.localScale;
            var mesh = PartBend.Straight(part);
            if (probe == null) probe = new GameObject("Drill Probe") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
            var collider = probe.TryGetComponent(out MeshCollider found) ? found : probe.AddComponent<MeshCollider>();
            collider.enabled = true;   // only while measuring (it sits at the origin)
            if (collider.sharedMesh != mesh) collider.sharedMesh = mesh;
            probe.transform.localScale = scale;
            var reach = mesh.bounds.extents.magnitude * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)) + 1f;
            var half = 0f;
            foreach (var way in new[] { axis, -axis })
                if (collider.Raycast(new Ray(center + way * reach, -way), out var hit, reach * 2f))
                    half = Mathf.Max(half, Mathf.Abs(Vector3.Dot(hit.point - center, axis)));
            collider.enabled = false;
            return half;
        }

        /// <summary>Drills a round hole straight through the part across its length (made in the straight part, so
        /// bends carry it), with a hole a screw goes through. False if nothing was cut.</summary>
        public static bool CrossDrill(GameObject part, Vector3 center, Vector3 axis, float diameter) {
            var (axisIndex, _, _, _) = PartBend.Frame(part, PartBend.Straight(part));
            var half = HalfWidth(part, center, axis, axisIndex);
            if (half < 0.01f) return false;
            var scale = part.transform.localScale;
            Vector3 Unscale(Vector3 v) => new(v.x / (scale.x == 0f ? 1f : scale.x), v.y / (scale.y == 0f ? 1f : scale.y), v.z / (scale.z == 0f ? 1f : scale.z));
            const float margin = 0.003f;
            var info = new HoleInfo {
                found = true,
                center = Unscale(center + axis * (half + margin)),
                normal = axis,
                thickness = 2f * (half + margin),
                holeRadius = diameter / 2f,
                round = true,
            };
            var straight = PartBend.Straight(part);
            var drilled = HoleDrilling.Drill(straight, info, diameter / 2f, null, HoleDrilling.DrillSegments, null);
            if (drilled == null) return false;
            var bent = PartBend.HasStraight(part);
            if (bent) PartBend.SetStraight(part, drilled);
            else {
                part.GetComponent<MeshFilter>().sharedMesh = drilled;
                if (part.TryGetComponent(out MeshCollider meshCollider)) meshCollider.sharedMesh = drilled;
            }
            var round = HoleShapes.instance.GetShapeMesh("circle");
            var hole = new GameObject("Drilled Hole", typeof(MeshCollider)) { tag = "HoleCollider", layer = HoleCollider.HOLE_COLLISIONS_LAYER };
            hole.GetComponent<MeshCollider>().sharedMesh = round;
            hole.transform.SetParent(part.transform, false);
            hole.transform.localPosition = Unscale(center);
            hole.transform.localRotation = Quaternion.LookRotation(axis);
            hole.transform.localScale = new Vector3(diameter, diameter, 2f * half);
            var marker = hole.AddComponent<DrilledHole>();
            marker.straightPosition = hole.transform.localPosition;
            marker.straightRotation = hole.transform.localRotation;
            var collider = hole.AddComponent<HoleCollider>();
            collider.holeType = HoleCollider.HoleType.Normal;
            collider.Refresh();
            if (bent) PartBend.Show(part, PartBend.SavedBends(part));
            return true;
        }

        public static void RecordCross(SavedObject saved, Vector3 center, Vector3 axis, float diameter) {
            string F(float f) => f.ToString("0.#####", CultureInfo.InvariantCulture);
            saved.state += CrossPrefix + string.Join(",", F(center.x), F(center.y), F(center.z), F(axis.x), F(axis.y), F(axis.z), F(diameter)) + ";";
        }

        private static bool TryParseCross(string entry, out Vector3 center, out Vector3 axis, out float diameter) {
            center = axis = Vector3.zero;
            diameter = 0f;
            if (entry == null || !entry.StartsWith(CrossPrefix)) return false;
            var v = entry.Substring(CrossPrefix.Length).Split(',');
            if (v.Length != 7) return false;
            var f = new float[7];
            for (var i = 0; i < 7; i++)
                if (!float.TryParse(v[i], NumberStyles.Float, CultureInfo.InvariantCulture, out f[i])) return false;
            center = new Vector3(f[0], f[1], f[2]);
            axis = new Vector3(f[3], f[4], f[5]);
            diameter = f[6];
            return true;
        }

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
            var radius = diameter / 2f;
            // V2: only the wall the hole is in, as far as the mesh goes (to the next face: one flange of a
            // channel, the whole case of a brain), and the new round wall only where there's material around it
            // (where the circle runs into an earlier cut or hole there's nothing to line).
            var near = new Wall(filter.sharedMesh, hole.localPosition, Mathf.Max(radius, Mathf.Max(hole.localScale.x, hole.localScale.y) * 0.75f) + depth * 4f + 0.25f);
            var (front, back) = near.Extent(hole.localPosition, normal, Mathf.Max(radius, Mathf.Max(hole.localScale.x, hole.localScale.y) * 0.5f) + 0.015f, depth);
            const float margin = 0.002f;
            var info = new HoleInfo {
                found = true,
                center = hole.localPosition + normal * (front + margin),
                normal = normal,
                thickness = front - back + 2f * margin,
                holeRadius = radius,
                round = true,
            };
            // Each side of the new hole is lined only as far as the material right there goes along it.
            var spans = near.Spans(hole.localPosition, normal, radius + 0.004f, HoleDrilling.DrillSegments, front, back, front + margin, depth);

            var drilled = HoleDrilling.Drill(filter.sharedMesh, info, radius, null, HoleDrilling.DrillSegments, spans);
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

        // V2: the mesh's triangles around a hole, for finding the wall it's in.
        private class Wall {
            private readonly List<(Vector3 a, Vector3 b, Vector3 c)> triangles = new();

            public Wall(Mesh mesh, Vector3 center, float reach) {
                var v = mesh.vertices;
                var t = mesh.triangles;
                var r2 = reach * reach;
                for (var i = 0; i + 2 < t.Length; i += 3) {
                    Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                    var box = new Bounds(a, Vector3.zero);
                    box.Encapsulate(b);
                    box.Encapsulate(c);
                    if (box.SqrDistance(center) <= r2) triangles.Add((a, b, c));
                }
            }

            // Where a line along the axis through a point crosses the mesh (distances along the axis, sorted).
            private List<float> Crossings(Vector3 point, Vector3 axis) {
                var result = new List<float>();
                foreach (var (a, b, c) in triangles)
                    if (Line(point, axis, a, b, c, out var d)) result.Add(d);
                result.Sort();
                // Shared edges give the same crossing twice.
                for (var i = result.Count - 1; i > 0; i--)
                    if (result[i] - result[i - 1] < 1e-5f) result.RemoveAt(i);
                return result;
            }

            /// <summary>How far the wall the hole is in goes in front of and behind its centre: the stretch of
            /// material around the hole (just outside it, all the way round) that the hole's middle is in or
            /// nearest.</summary>
            public (float front, float back) Extent(Vector3 center, Vector3 axis, float ringRadius, float depth) {
                Basis(axis, out var u, out var w);
                float front = float.MinValue, back = float.MaxValue;
                for (var k = 0; k < 16; k++) {
                    var angle = k * Mathf.PI * 2f / 16f;
                    var crossings = Crossings(center + (u * Mathf.Cos(angle) + w * Mathf.Sin(angle)) * ringRadius, axis);
                    // Material between each pair of crossings; the pair around the middle (or nearest to it).
                    float bestGap = float.MaxValue, bestA = 0f, bestB = 0f;
                    for (var i = 0; i + 1 < crossings.Count; i += 2) {
                        float a = crossings[i], b = crossings[i + 1];
                        var gap = a > 0f ? a : b < 0f ? -b : 0f;
                        if (gap < bestGap) {
                            bestGap = gap;
                            bestA = a;
                            bestB = b;
                        }
                    }
                    if (bestGap > depth) continue;  // nothing here near the hole (an edge, a cut)
                    front = Mathf.Max(front, bestB);
                    back = Mathf.Min(back, bestA);
                }
                if (front < back) return (depth / 2f, -depth / 2f);
                return (Mathf.Max(front, depth / 2f), Mathf.Min(back, -depth / 2f));
            }

            /// <summary>
            /// For each side of the new hole, the stretch of material just outside it along the hole (the one the
            /// hole's own wall is in, or nearest), kept within front..back, as (top, bottom) from the drill's top
            /// face at <paramref name="topAt"/>; (0, 0) where there's none. Null when the mesh can't tell (not
            /// closed): then the whole hole is lined.
            /// </summary>
            public Vector2[] Spans(Vector3 center, Vector3 axis, float radius, int segments, float front, float back, float topAt, float depth) {
                Basis(axis, out var u, out var w);
                var result = new Vector2[segments];
                var any = false;
                for (var k = 0; k < segments; k++) {
                    var angle = (k + 0.5f) * Mathf.PI * 2f / segments;
                    var crossings = Crossings(center + (u * Mathf.Cos(angle) + w * Mathf.Sin(angle)) * radius, axis);
                    float bestGap = float.MaxValue, bestA = 0f, bestB = 0f;
                    for (var i = 0; i + 1 < crossings.Count; i += 2) {
                        float a = crossings[i], b = crossings[i + 1];
                        var gap = a > 0f ? a : b < 0f ? -b : 0f;
                        if (gap < bestGap) {
                            bestGap = gap;
                            bestA = a;
                            bestB = b;
                        }
                    }
                    if (bestGap > depth) continue;  // open air on this side (an edge, a cut, another hole)
                    var top = Mathf.Min(bestB, front) - topAt;
                    var bottom = Mathf.Max(bestA, back) - topAt;
                    if (top - bottom < 0.0005f) continue;
                    result[k] = new Vector2(top, bottom);
                    any = true;
                }
                return any ? result : null;
            }

            private static void Basis(Vector3 n, out Vector3 u, out Vector3 w) {
                u = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                w = Vector3.Cross(n, u);
            }

            private static bool Line(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float distance) {
                distance = 0f;
                var e1 = b - a;
                var e2 = c - a;
                var p = Vector3.Cross(direction, e2);
                var det = Vector3.Dot(e1, p);
                if (Mathf.Abs(det) < 1e-12f) return false;
                var inv = 1f / det;
                var s = origin - a;
                var u = Vector3.Dot(s, p) * inv;
                if (u < 0f || u > 1f) return false;
                var q = Vector3.Cross(s, e1);
                var v = Vector3.Dot(direction, q) * inv;
                if (v < 0f || u + v > 1f) return false;
                distance = Vector3.Dot(e2, q) * inv;
                return true;
            }
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
                if (GearChamfer.ApplyEntry(part, entry)) continue;   // chamfered gear teeth
                if (GearTeeth.ApplyEntry(part, entry)) continue;     // V2: teeth taken off
                if (PartBend.TryParse(entry, out _)) continue;        // V2: bends, all at once below
                if (TryParseCross(entry, out var crossCenter, out var crossAxis, out var crossDiameter)) {   // V2: holes across
                    CrossDrill(part, crossCenter, crossAxis, crossDiameter);
                    continue;
                }
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
            PartBend.ApplySaved(part);   // V2
        }
    }

    /// <summary>V2: a hole drilled across a part: where it is on the straight part (bends carry it).</summary>
    public class DrilledHole : MonoBehaviour {
        public Vector3 straightPosition;
        public Quaternion straightRotation = Quaternion.identity;
    }

    // Undo/redo of a drilled hole: puts back the part's mesh, its holes' shapes and its saved state.
    public class DrillElement : IElement {
        private readonly GameObject part;
        private readonly Mesh mesh;
        private readonly string state;
        private readonly List<(MeshCollider collider, Mesh shape, Vector3 scale)> holes = new();
        private readonly Mesh straight;   // V2: a bent part's mesh before its bends
        private readonly List<GameObject> drilled;   // V2: holes drilled across it

        public DrillElement(GameObject part) {
            this.part = part;
            mesh = part.GetComponent<MeshFilter>().sharedMesh;
            state = part.GetComponent<SavedObject>().state;
            if (PartBend.HasStraight(part)) straight = PartBend.Straight(part);
            drilled = part.GetComponentsInChildren<DrilledHole>(true).Where(h => h.gameObject.activeSelf).Select(h => h.gameObject).ToList();
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
            if (straight != null) PartBend.SetStraight(part, straight);
            foreach (var hole in part.GetComponentsInChildren<DrilledHole>(true))
                hole.gameObject.SetActive(drilled.Contains(hole.gameObject));
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
