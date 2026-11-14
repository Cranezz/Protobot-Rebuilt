using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Parts_List;
using Protobot.Geometry;
using Protobot.StateSystems;
using UnityEngine;

namespace Protobot.Tools {
    // V2: taking teeth off a gear or sprocket, as teams sand or grind them (a slip gear for a catapult or a puncher):
    // the chosen teeth are cut down to the root circle (the bottom of the gaps beside them) and no further, and the
    // teeth beside them are left whole. Each tooth is cut out as the wedge between the middles of the two gaps beside
    // it (two planes through the axle), that wedge trimmed at the root circle and put back. Kept in the part's saved
    // state as "tooth:centre (3),axle (3),angle,root,half pitch;" (part space) and made again on load with the cuts,
    // chamfers and drilled holes, in order (PartDrilling.ApplySaved).
    public static class GearTeeth {
        public const string Prefix = "tooth:";
        private const float MetresPerUnit = 0.0254f;

        /// <summary>A gear's teeth in its own space: the middle of the gear, its axle, the two directions across it,
        /// the tip and root radius, the number of teeth and the first tooth's angle (radians from <c>a</c> toward
        /// <c>b</c>).</summary>
        public struct Gear {
            public Vector3 center, axis, a, b;
            public float tip, root, phase, thickness;
            public int teeth;

            public float HalfPitch => Mathf.PI / Mathf.Max(1, teeth);
            public float AngleOf(int k) => phase + k * 2f * HalfPitch;
            public Vector3 Radial(float angle) => a * Mathf.Cos(angle) + b * Mathf.Sin(angle);
        }

        public static bool Measure(GameObject part, out Gear gear) {
            gear = default;
            var teeth = Motion.GearTrain.Teeth(part, out _);
            if (teeth < 3 || !part.TryGetComponent(out MeshFilter filter)) return false;
            var faces = GearChamfer.Faces(part);
            var axis = faces[0].inward;
            var center = (faces[0].center + faces[1].center) / 2f;
            var a = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var b = Vector3.Cross(axis, a).normalized;
            var mesh = filter.sharedMesh;
            float tip = 0f;
            var vertices = mesh.vertices;
            foreach (var v in vertices) {
                var p = v - center;
                tip = Mathf.Max(tip, (p - axis * Vector3.Dot(p, axis)).magnitude);
            }
            // The teeth's phase: the circular mean of the tip points' angles, T times round.
            float sx = 0f, sy = 0f;
            foreach (var v in vertices) {
                var p = v - center;
                p -= axis * Vector3.Dot(p, axis);
                if (p.magnitude < tip - 0.01f) continue;
                var angle = Mathf.Atan2(Vector3.Dot(p, b), Vector3.Dot(p, a)) * teeth;
                sx += Mathf.Cos(angle);
                sy += Mathf.Sin(angle);
            }
            var phase = Mathf.Atan2(sy, sx) / teeth;
            gear = new Gear { center = center, axis = axis, a = a, b = b, tip = tip, phase = phase, teeth = teeth,
                              thickness = Vector3.Dot(faces[1].center - faces[0].center, axis) };
            // The root: rays in from outside down the middle of a few gaps, onto the gear.
            var probe = new GameObject("Teeth Probe") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
            var collider = probe.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            var roots = new List<float>();
            for (var k = 0; k < Mathf.Min(teeth, 8); k++) {
                var gap = gear.AngleOf(k * teeth / Mathf.Min(teeth, 8)) + gear.HalfPitch;
                var radial = gear.Radial(gap);
                var from = center + radial * (tip + 1f);
                if (collider.Raycast(new Ray(from, -radial), out var hit, tip + 1f)) roots.Add(Vector3.Dot(hit.point - center, radial));
            }
            Object.DestroyImmediate(probe);
            if (roots.Count == 0) return false;
            roots.Sort();
            gear.root = roots[roots.Count / 2];
            return gear.root > 0.05f && gear.root < tip - 0.01f;
        }

        /// <summary>Cuts one tooth (centred at <paramref name="angle"/>) down to the root circle.</summary>
        public static bool Remove(GameObject part, Gear gear, float angle) {
            var filter = part.GetComponent<MeshFilter>();
            var source = filter.sharedMesh;
            var subCount = source.subMeshCount;
            var metres = Scaled(source, MetresPerUnit);
            Plane P(Vector3 normal, Vector3 point) => new(normal.normalized, point * MetresPerUnit);
            var lo = angle - gear.HalfPitch;
            var hi = angle + gear.HalfPitch;
            // The wedge: past the plane through the axle at the gap before the tooth, and short of the one after it.
            var normalA = Vector3.Cross(gear.axis, gear.Radial(lo));   // toward bigger angles
            if (Vector3.Dot(normalA, gear.Radial(angle)) < 0f) normalA = -normalA;
            var normalB = Vector3.Cross(gear.axis, gear.Radial(hi));
            if (Vector3.Dot(normalB, gear.Radial(angle)) < 0f) normalB = -normalB;
            MeshSlicer.SliceBoth(metres, P(normalA, gear.center), true, out var past, out var before, out _);
            Object.DestroyImmediate(metres);
            if (past == null) return false;
            MeshSlicer.SliceBoth(past, P(normalB, gear.center), true, out var wedge, out var after, out _);
            Object.DestroyImmediate(past);
            if (wedge == null) return false;
            // The tooth: everything in the wedge beyond the root circle's tangent at the tooth.
            var radial = gear.Radial(angle);
            var inner = MeshSlicer.Slice(wedge, P(-radial, gear.center + radial * (gear.root + 0.002f)));
            Object.DestroyImmediate(wedge);
            var merged = Merge(new[] { before, after, inner }, subCount);
            foreach (var piece in new[] { before, after, inner }) if (piece != null) Object.DestroyImmediate(piece);
            if (merged == null) return false;
            var mesh = Scaled(merged, 1f / MetresPerUnit);
            Object.DestroyImmediate(merged);
            mesh.name = source.name;
            var volume = MeshSlicer.Volume(source);
            filter.sharedMesh = mesh;
            if (part.TryGetComponent(out MeshCollider meshCollider)) meshCollider.sharedMesh = mesh;
            if (part.TryGetComponent(out PartName partName) && volume > 0f)
                partName.weightInGrams = Mathf.Round(partName.weightInGrams * MeshSlicer.Volume(mesh) / volume * 100f) / 100f;
            return true;
        }

        private static Mesh Scaled(Mesh source, float k) {
            var mesh = Object.Instantiate(source);
            var v = mesh.vertices;
            for (var i = 0; i < v.Length; i++) v[i] *= k;
            mesh.vertices = v;
            mesh.RecalculateBounds();
            return mesh;
        }

        // The pieces as one mesh, each piece's submeshes kept in the same slot (a cut's cap goes in the last).
        private static Mesh Merge(IEnumerable<Mesh> pieces, int subCount) {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var subs = Enumerable.Range(0, Mathf.Max(1, subCount)).Select(_ => new List<int>()).ToList();
            foreach (var piece in pieces) {
                if (piece == null || piece.vertexCount == 0) continue;
                var offset = vertices.Count;
                vertices.AddRange(piece.vertices);
                var n = piece.normals;
                normals.AddRange(n.Length == piece.vertexCount ? n : new Vector3[piece.vertexCount]);
                var uv = piece.uv;
                uvs.AddRange(uv.Length == piece.vertexCount ? uv : new Vector2[piece.vertexCount]);
                for (var s = 0; s < piece.subMeshCount; s++)
                    subs[Mathf.Min(s, subs.Count - 1)].AddRange(piece.GetTriangles(s).Select(i => i + offset));
            }
            if (vertices.Count == 0) return null;
            var mesh = new Mesh { indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = subs.Count;
            for (var s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        public static void Record(SavedObject saved, Gear gear, float angle) {
            saved.state += Prefix + string.Join(",", new[] {
                gear.center.x, gear.center.y, gear.center.z, gear.axis.x, gear.axis.y, gear.axis.z, angle, gear.root, gear.HalfPitch
            }.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + ";";
        }

        /// <summary>Makes a saved "tooth:" entry again (false if it isn't one).</summary>
        public static bool ApplyEntry(GameObject part, string entry) {
            if (!entry.StartsWith(Prefix)) return false;
            var values = entry.Substring(Prefix.Length).Split(',');
            if (values.Length != 9) return true;
            var f = new float[9];
            for (var i = 0; i < 9; i++)
                if (!float.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out f[i])) return true;
            var axis = new Vector3(f[3], f[4], f[5]);
            var a = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var gear = new Gear { center = new Vector3(f[0], f[1], f[2]), axis = axis, a = a, b = Vector3.Cross(axis, a).normalized, root = f[7],
                                  teeth = Mathf.Max(3, Mathf.RoundToInt(Mathf.PI / Mathf.Max(0.001f, f[8]))) };
            if (Remove(part, gear, f[6])) MarkName(part);
            return true;
        }

        public static void MarkName(GameObject part) {
            if (part.TryGetComponent(out PartName partName) && !partName.name.Contains("teeth removed"))
                partName.name += " (teeth removed)";
        }

        /// <summary>Takes off the chosen teeth as one undo step. The message, and whether it worked.</summary>
        public static (string message, bool ok) Apply(GameObject part, Gear gear, ICollection<int> teeth) {
            if (teeth.Count == 0) return ("Click the teeth to take off first", false);
            if (teeth.Count >= gear.teeth) return ("That's every tooth: pick fewer (or cut the gear round with the saw)", false);
            var before = new ChamferElement(part);
            var saved = part.GetComponent<SavedObject>();
            var done = 0;
            foreach (var k in teeth.OrderBy(k => k)) {
                var angle = gear.AngleOf(k);
                if (!Remove(part, gear, angle)) continue;
                Record(saved, gear, angle);
                done++;
            }
            if (done == 0) return ("Couldn't cut those teeth", false);
            MarkName(part);
            StateSystem.AddElement(before);
            StateSystem.AddState(new State(new List<IElement> { new ChamferElement(part) }));
            Object.FindAnyObjectByType<PartListOutput>()?.CalculatePartsList();
            return ("Took off " + done + (done == 1 ? " tooth" : " teeth") + " down to the root (" + (gear.teeth - done) + " left)", true);
        }
    }
}
