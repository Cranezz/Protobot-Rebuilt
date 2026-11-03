using System.Collections.Generic;
using System.Linq;
using Protobot.Geometry;
using UnityEngine;

namespace Protobot.Polycarb {
    // V2: the closed areas of a sketch, like the contours a CAD program lets you pick to extrude. All curves are
    // split where they cross, loose ends are dropped, and the faces between them are traced (each edge has a face
    // on either side). A face's holes are the separate shapes drawn inside it. Picking faces and joining them
    // (Outline) gives the piece's outer outlines and holes.
    public class Regions {
        public class Face {
            public int index;
            public List<int> halfEdges = new();
            public List<Vector2> polygon = new();
            public float area;              // > 0: a closed area; < 0: the outside of a shape
            public int component;
            public List<Face> holes = new(); // outsides of shapes drawn inside this area
            public Face parent;              // (for outsides) the area they're drawn inside
            public float AreaWithoutHoles => area + holes.Sum(h => h.area);
        }

        private readonly List<Vector2> vertices = new();
        private readonly List<int> from = new(), to = new(), twin = new(), next = new(), faceOf = new();
        public readonly List<Face> faces = new();
        public IEnumerable<Face> Areas => faces.Where(f => f.area > 0f);

        private const float Snap = 1e-4f;

        public static Regions Of(SketchData sketch) {
            var regions = new Regions();
            regions.Build(sketch);
            return regions;
        }

        private void Build(SketchData sketch) {
            // Segments of every curve that isn't construction geometry.
            var segments = new List<(Vector2 a, Vector2 b)>();
            foreach (var e in sketch.entities.Where(e => !e.construction)) {
                var line = sketch.Polyline(e, out var closed);
                for (var i = 0; i + 1 < line.Count; i++) segments.Add((line[i], line[i + 1]));
                if (closed && line.Count > 2) segments.Add((line[^1], line[0]));
            }
            segments.RemoveAll(s => (s.b - s.a).sqrMagnitude < 1e-10f);

            // Split where they cross (or touch).
            var cuts = segments.Select(_ => new List<float> { 0f, 1f }).ToList();
            for (var i = 0; i < segments.Count; i++) {
                var (a, b) = segments[i];
                var minA = Vector2.Min(a, b) - Vector2.one * Snap;
                var maxA = Vector2.Max(a, b) + Vector2.one * Snap;
                for (var j = i + 1; j < segments.Count; j++) {
                    var (c, d) = segments[j];
                    if (Mathf.Max(c.x, d.x) < minA.x || Mathf.Min(c.x, d.x) > maxA.x || Mathf.Max(c.y, d.y) < minA.y || Mathf.Min(c.y, d.y) > maxA.y)
                        continue;
                    Intersect(a, b, c, d, cuts[i], cuts[j]);
                }
            }

            // Vertices (merged when within Snap) and edges.
            var ids = new Dictionary<Vector2Int, int>();
            int Vertex(Vector2 p) {
                var key = new Vector2Int(Mathf.RoundToInt(p.x / Snap), Mathf.RoundToInt(p.y / Snap));
                for (var dx = -1; dx <= 1; dx++)
                    for (var dy = -1; dy <= 1; dy++)
                        if (ids.TryGetValue(key + new Vector2Int(dx, dy), out var near)) return near;
                ids[key] = vertices.Count;
                vertices.Add(p);
                return vertices.Count - 1;
            }
            var edges = new HashSet<(int, int)>();
            for (var i = 0; i < segments.Count; i++) {
                var (a, b) = segments[i];
                var ts = cuts[i].Distinct().OrderBy(t => t).ToList();
                for (var k = 0; k + 1 < ts.Count; k++) {
                    var u = Vertex(Vector2.Lerp(a, b, ts[k]));
                    var v = Vertex(Vector2.Lerp(a, b, ts[k + 1]));
                    if (u != v) edges.Add(u < v ? (u, v) : (v, u));
                }
            }

            // Loose ends bound nothing.
            var degree = new int[vertices.Count];
            foreach (var (u, v) in edges) { degree[u]++; degree[v]++; }
            bool removed;
            do {
                removed = false;
                foreach (var edge in edges.ToList()) {
                    if (degree[edge.Item1] > 1 && degree[edge.Item2] > 1) continue;
                    edges.Remove(edge);
                    degree[edge.Item1]--;
                    degree[edge.Item2]--;
                    removed = true;
                }
            } while (removed);

            // Half-edges, and around each vertex its outgoing ones by angle.
            foreach (var (u, v) in edges) {
                from.Add(u); to.Add(v); twin.Add(from.Count);
                from.Add(v); to.Add(u); twin.Add(from.Count - 2);
            }
            var outgoing = new List<int>[vertices.Count];
            for (var h = 0; h < from.Count; h++) (outgoing[from[h]] ??= new List<int>()).Add(h);
            foreach (var list in outgoing)
                list?.Sort((x, y) => Angle(x).CompareTo(Angle(y)));
            // The face on the left of u→v continues at v with the edge just clockwise of v→u.
            for (var h = 0; h < from.Count; h++) {
                var around = outgoing[to[h]];
                var back = around.IndexOf(twin[h]);
                next.Add(around[(back - 1 + around.Count) % around.Count]);
                faceOf.Add(-1);
            }

            // Faces.
            for (var h = 0; h < from.Count; h++) {
                if (faceOf[h] >= 0) continue;
                var face = new Face { index = faces.Count };
                var g = h;
                var guard = 0;
                do {
                    faceOf[g] = face.index;
                    face.halfEdges.Add(g);
                    face.polygon.Add(vertices[from[g]]);
                    g = next[g];
                } while (g != h && ++guard < 100000);
                face.area = SignedArea(face.polygon);
                faces.Add(face);
            }

            // Which shapes are drawn inside which areas.
            var component = Components();
            foreach (var face in faces) face.component = component[from[face.halfEdges[0]]];
            var areas = faces.Where(f => f.area > 0f).OrderBy(f => f.area).ToList();
            foreach (var outside in faces.Where(f => f.area <= 0f)) {
                var probe = outside.polygon[0];
                outside.parent = areas.FirstOrDefault(f => f.component != outside.component && MeshSlicer.Contains(f.polygon, probe));
                outside.parent?.holes.Add(outside);
            }
        }

        private float Angle(int h) {
            var d = vertices[to[h]] - vertices[from[h]];
            return Mathf.Atan2(d.y, d.x);
        }

        private int[] Components() {
            var parent = Enumerable.Range(0, vertices.Count).ToArray();
            int Find(int x) {
                while (parent[x] != x) x = parent[x] = parent[parent[x]];
                return x;
            }
            for (var h = 0; h < from.Count; h += 2) parent[Find(from[h])] = Find(to[h]);
            return Enumerable.Range(0, vertices.Count).Select(Find).ToArray();
        }

        private static void Intersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d, List<float> ab, List<float> cd) {
            var r = b - a;
            var s = d - c;
            var denominator = r.x * s.y - r.y * s.x;
            var lengthR = r.magnitude;
            var lengthS = s.magnitude;
            if (Mathf.Abs(denominator) < 1e-10f) {
                // Parallel: overlapping collinear pieces split at each other's ends.
                if (Mathf.Abs((c - a).x * r.y - (c - a).y * r.x) / lengthR > Snap) return;
                foreach (var p in new[] { c, d }) {
                    var t = Vector2.Dot(p - a, r) / (lengthR * lengthR);
                    if (t > 0f && t < 1f) ab.Add(t);
                }
                foreach (var p in new[] { a, b }) {
                    var u = Vector2.Dot(p - c, s) / (lengthS * lengthS);
                    if (u > 0f && u < 1f) cd.Add(u);
                }
                return;
            }
            var qp = c - a;
            var t1 = (qp.x * s.y - qp.y * s.x) / denominator;
            var u1 = (qp.x * r.y - qp.y * r.x) / denominator;
            var slackT = Snap / lengthR;
            var slackU = Snap / lengthS;
            if (t1 < -slackT || t1 > 1f + slackT || u1 < -slackU || u1 > 1f + slackU) return;
            ab.Add(Mathf.Clamp01(t1));
            cd.Add(Mathf.Clamp01(u1));
        }

        public static float SignedArea(IList<Vector2> polygon) {
            var area = 0f;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
                area += (polygon[j].x - polygon[i].x) * (polygon[j].y + polygon[i].y);
            return area / 2f;
        }

        // ------------------------------------------------------------------ picking

        /// <summary>The smallest area containing the point (not in one of its holes), or null.</summary>
        public Face AreaAt(Vector2 point) {
            foreach (var face in Areas.OrderBy(f => f.area)) {
                if (!MeshSlicer.Contains(face.polygon, point)) continue;
                if (face.holes.Any(h => MeshSlicer.Contains(h.polygon, point))) continue;
                return face;
            }
            return null;
        }

        /// <summary>
        /// The outline of the chosen areas joined into one shape: outer loops (counter-clockwise) and hole loops
        /// (clockwise). Edges between two chosen areas disappear.
        /// </summary>
        public List<List<Vector2>> Outline(ICollection<Face> chosen) {
            var set = new HashSet<int>(chosen.Select(f => f.index));
            var loops = new List<List<Vector2>>();
            var used = new HashSet<int>();
            bool Boundary(int h) => set.Contains(faceOf[h]) && !set.Contains(faceOf[twin[h]]);
            for (var h = 0; h < from.Count; h++) {
                if (!Boundary(h) || used.Contains(h)) continue;
                var loop = new List<Vector2>();
                var g = h;
                var guard = 0;
                do {
                    used.Add(g);
                    loop.Add(vertices[from[g]]);
                    // On round the end vertex to the next edge that's still on the boundary.
                    var candidate = next[g];
                    var turns = 0;
                    while (!Boundary(candidate) && turns++ < 64) candidate = next[twin[candidate]];
                    g = candidate;
                } while (g != h && ++guard < 100000);
                if (loop.Count >= 3) loops.Add(loop);
            }
            // Shapes inside a chosen area that weren't chosen themselves are holes in it.
            foreach (var face in chosen)
                foreach (var hole in face.holes) {
                    var inside = faces.Where(f => f.area > 0f && f.component == hole.component).ToList();
                    if (inside.Any(f => set.Contains(f.index))) continue;
                    var loop = new List<Vector2>(hole.polygon);
                    loops.Add(loop);
                }
            return loops.Select(Simplify).Where(l => l.Count >= 3).ToList();
        }

        // Drops points on straight runs (a split edge) so the outline has only its real corners.
        private static List<Vector2> Simplify(List<Vector2> loop) {
            var result = new List<Vector2>();
            for (var i = 0; i < loop.Count; i++) {
                var prev = loop[(i - 1 + loop.Count) % loop.Count];
                var point = loop[i];
                var after = loop[(i + 1) % loop.Count];
                var cross = (point.x - prev.x) * (after.y - point.y) - (point.y - prev.y) * (after.x - point.x);
                if (Mathf.Abs(cross) > 1e-7f || Vector2.Dot(point - prev, after - point) < 0f) result.Add(point);
            }
            return result;
        }
    }
}
