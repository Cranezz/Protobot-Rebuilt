using System.Collections.Generic;
using UnityEngine;

namespace Protobot.Motion {
    // V2: shared by rubber bands, latex tubing and pneumatic tubing: meshes along a line (a round tube or a flat
    // ribbon), rounded corners for a line of points (a tube bends with a radius, it doesn't kink), the plane that best
    // fits some points, and the part a point lies on.
    public static class Strands {
        /// <summary>A round tube along a line (closed: back to its start). up: a direction kept square to the line
        /// (a planar loop's normal) so the rings don't twist; otherwise the rings are carried along the line.</summary>
        public static void Tube(IList<Vector3> line, bool closed, float radius, int sides, List<Vector3> vertices, List<Vector3> normals, List<int> triangles, Vector3? up = null) {
            var n = line.Count;
            if (n < 2) return;
            var start = vertices.Count;
            Vector3 frame = default;
            for (var i = 0; i < n; i++) {
                var tangent = Tangent(line, i, closed);
                if (up.HasValue) frame = Vector3.ProjectOnPlane(up.Value, tangent);
                else if (i == 0) frame = Vector3.ProjectOnPlane(Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right, tangent);
                else frame = Vector3.ProjectOnPlane(frame, tangent);
                if (frame.sqrMagnitude < 1e-10f) frame = Vector3.ProjectOnPlane(Vector3.right, tangent);
                frame.Normalize();
                var side = Vector3.Cross(tangent, frame);
                for (var k = 0; k <= sides; k++) {
                    var angle = k * Mathf.PI * 2f / sides;
                    var normal = frame * Mathf.Cos(angle) + side * Mathf.Sin(angle);
                    vertices.Add(line[i] + normal * radius);
                    normals.Add(normal);
                }
            }
            var rings = closed ? n : n - 1;
            for (var i = 0; i < rings; i++) {
                var a = start + i * (sides + 1);
                var b = start + (i + 1) % n * (sides + 1);
                for (var k = 0; k < sides; k++)
                    // Wound so the faces look outward (they faced in: the tube showed its inside).
                    triangles.AddRange(new[] { a + k, a + k + 1, b + k, a + k + 1, b + k + 1, b + k });
            }
        }

        /// <summary>A flat strip along a closed line in a plane: width across the plane (along its normal), thickness
        /// in it. A rubber band seen edge-on.</summary>
        public static void Ribbon(IList<Vector3> line, Vector3 normal, float width, float thickness, List<Vector3> vertices, List<Vector3> normals, List<int> triangles) {
            var n = line.Count;
            if (n < 3) return;
            var w = width / 2f;
            var t = thickness / 2f;
            // Four faces (outer, inner, the two edges), each its own strip for flat shading.
            var corners = new (float across, float outward)[] { (w, t), (-w, t), (-w, -t), (w, -t) };
            var faceNormals = new (float across, float outward)[] { (0f, 1f), (-1f, 0f), (0f, -1f), (1f, 0f) };
            for (var face = 0; face < 4; face++) {
                var start = vertices.Count;
                var (a0, o0) = corners[face];
                var (a1, o1) = corners[(face + 1) % 4];
                var (fa, fo) = faceNormals[face];
                for (var i = 0; i < n; i++) {
                    var tangent = Tangent(line, i, true);
                    var outward = Vector3.Cross(tangent, normal).normalized;
                    var faceNormal = (normal * fa + outward * fo).normalized;
                    vertices.Add(line[i] + normal * a0 + outward * o0);
                    vertices.Add(line[i] + normal * a1 + outward * o1);
                    normals.Add(faceNormal);
                    normals.Add(faceNormal);
                }
                for (var i = 0; i < n; i++) {
                    var p = start + i * 2;
                    var q = start + (i + 1) % n * 2;
                    triangles.AddRange(new[] { p, p + 1, q, q, p + 1, q + 1 });
                }
            }
        }

        public static Vector3 Tangent(IList<Vector3> line, int i, bool closed) {
            var n = line.Count;
            Vector3 before, after;
            if (closed) {
                before = line[(i - 1 + n) % n];
                after = line[(i + 1) % n];
            }
            else {
                before = line[Mathf.Max(0, i - 1)];
                after = line[Mathf.Min(n - 1, i + 1)];
            }
            var d = after - before;
            return d.sqrMagnitude > 1e-12f ? d.normalized : Vector3.forward;
        }

        /// <summary>The line through the points with each corner rounded to the radius (less where the legs beside it
        /// are too short for it): what a tube bent round them looks like.</summary>
        public static List<Vector3> Rounded(IList<Vector3> points, float radius, float stepDegrees = 8f) {
            var result = new List<Vector3>();
            if (points.Count == 0) return result;
            result.Add(points[0]);
            for (var i = 1; i < points.Count - 1; i++) {
                var p = points[i];
                var inLeg = p - points[i - 1];
                var outLeg = points[i + 1] - p;
                if (inLeg.sqrMagnitude < 1e-10f || outLeg.sqrMagnitude < 1e-10f) continue;
                var a = inLeg.normalized;
                var b = outLeg.normalized;
                var turn = Mathf.Acos(Mathf.Clamp(Vector3.Dot(a, b), -1f, 1f));
                if (turn < 0.01f) {
                    result.Add(p);
                    continue;
                }
                var cut = radius * Mathf.Tan(Mathf.Min(turn, 3.0f) / 2f);
                cut = Mathf.Min(cut, inLeg.magnitude * 0.5f, outLeg.magnitude * 0.5f);
                var from = p - a * cut;
                var to = p + b * cut;
                var steps = Mathf.Max(2, Mathf.CeilToInt(turn * Mathf.Rad2Deg / stepDegrees));
                for (var k = 0; k <= steps; k++) {
                    var s = k / (float)steps;
                    // A quadratic curve on the corner: close to a circular arc, tangent to both legs.
                    result.Add((1 - s) * (1 - s) * from + 2 * (1 - s) * s * p + s * s * to);
                }
            }
            if (points.Count > 1) result.Add(points[^1]);
            return result;
        }

        public static float Length(IList<Vector3> line, bool closed = false) {
            var total = 0f;
            for (var i = 0; i + 1 < line.Count; i++) total += Vector3.Distance(line[i], line[i + 1]);
            if (closed && line.Count > 1) total += Vector3.Distance(line[^1], line[0]);
            return total;
        }

        /// <summary>The direction most nearly square to all the vectors (smallest eigenvector of Σ v vᵀ + extra),
        /// starting from (and kept on the side of) a guess.</summary>
        public static Vector3 SmallestDirection(Matrix4x4 m, Vector3 guess) {
            // Power iteration on (s·I − M): its largest eigenvector is M's smallest.
            var s = m[0, 0] + m[1, 1] + m[2, 2] + 1e-6f;
            var v = guess.sqrMagnitude > 1e-10f ? guess.normalized : Vector3.up;
            for (var k = 0; k < 80; k++) {
                var w = new Vector3(
                    s * v.x - (m[0, 0] * v.x + m[0, 1] * v.y + m[0, 2] * v.z),
                    s * v.y - (m[1, 0] * v.x + m[1, 1] * v.y + m[1, 2] * v.z),
                    s * v.z - (m[2, 0] * v.x + m[2, 1] * v.y + m[2, 2] * v.z));
                if (w.sqrMagnitude < 1e-20f) break;
                v = w.normalized;
            }
            return Vector3.Dot(v, guess) < 0f ? -v : v;
        }

        public static Matrix4x4 Outer(Vector3 a, Vector3 b, float weight = 1f) {
            var m = Matrix4x4.zero;
            for (var r = 0; r < 3; r++)
                for (var c = 0; c < 3; c++)
                    m[r, c] = a[r] * b[c] * weight;
            return m;
        }

        public static Matrix4x4 Add(Matrix4x4 a, Matrix4x4 b) {
            var m = Matrix4x4.zero;
            for (var r = 0; r < 3; r++)
                for (var c = 0; c < 3; c++)
                    m[r, c] = a[r, c] + b[r, c];
            return m;
        }

        /// <summary>The build part whose collider is nearest a point (within reach), skipping some.</summary>
        public static GameObject PartAt(Vector3 point, float reach, System.Func<GameObject, bool> skip) {
            GameObject best = null;
            var bestDistance = reach;
            foreach (var collider in Physics.OverlapSphere(point, reach, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore)) {
                var saved = collider.GetComponentInParent<SavedObject>();
                if (saved == null || saved.gameObject.IsDeleted()) continue;
                var part = SelectAs.Resolve(collider.gameObject) ?? saved.gameObject;
                if (skip != null && skip(part)) continue;
                var d = collider is MeshCollider mesh && !mesh.convex
                    ? Vector3.Distance(collider.bounds.ClosestPoint(point), point)
                    : Vector3.Distance(collider.ClosestPoint(point), point);
                if (d < bestDistance) {
                    bestDistance = d;
                    best = part;
                }
            }
            return best;
        }

        // ------------------------------------------------------------------ how round a part is at a point

        private class Profile {
            public float[] t;
            public Vector2[] flat;
            public int[] triangles;
            public readonly Dictionary<int, float> known = new();
        }

        private static readonly Dictionary<string, Profile> profiles = new();

        /// <summary>How far the part's surface reaches from a line through it (its own space) where it crosses the
        /// line at a point (the most of a slice a little either side): a screw's shank or head, a standoff's hex
        /// corners, a spacer's or wheel's rim. Exact for prisms, whose points are only at their ends.</summary>
        public static float RadiusAt(GameObject part, Vector3 originLocal, Vector3 axisLocal, float atLocal, float window = 0.04f) {
            if (part == null || !part.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null || !filter.sharedMesh.isReadable) return 0.1f;
            var mesh = filter.sharedMesh;
            axisLocal.Normalize();
            var key = mesh.GetInstanceID() + "|" + originLocal.ToString("F3") + "|" + axisLocal.ToString("F3");
            if (!profiles.TryGetValue(key, out var profile)) {
                var vertices = mesh.vertices;
                var u = Vector3.Cross(axisLocal, Mathf.Abs(axisLocal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                var v = Vector3.Cross(axisLocal, u);
                profile = new Profile { t = new float[vertices.Length], flat = new Vector2[vertices.Length], triangles = mesh.triangles };
                for (var i = 0; i < vertices.Length; i++) {
                    var d = vertices[i] - originLocal;
                    profile.t[i] = Vector3.Dot(d, axisLocal);
                    profile.flat[i] = new Vector2(Vector3.Dot(d, u), Vector3.Dot(d, v));
                }
                profiles[key] = profile;
            }
            var bin = Mathf.RoundToInt(atLocal / 0.02f);
            if (profile.known.TryGetValue(bin, out var cached)) return cached;
            var radius = 0f;
            var tris = profile.triangles;
            foreach (var at in new[] { bin * 0.02f - window, bin * 0.02f, bin * 0.02f + window })
                for (var i = 0; i + 2 < tris.Length; i += 3)
                    for (var e = 0; e < 3; e++) {
                        int a = tris[i + e], b = tris[i + (e + 1) % 3];
                        float ta = profile.t[a] - at, tb = profile.t[b] - at;
                        if (ta * tb > 0f) continue;
                        var s = Mathf.Abs(ta - tb) < 1e-7f ? 0f : ta / (ta - tb);
                        radius = Mathf.Max(radius, Vector2.Lerp(profile.flat[a], profile.flat[b], s).magnitude);
                    }
            profile.known[bin] = radius;
            return radius;
        }
    }
}
