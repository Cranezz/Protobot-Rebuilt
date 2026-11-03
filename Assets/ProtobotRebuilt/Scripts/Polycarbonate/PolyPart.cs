using System;
using System.Collections.Generic;
using System.Linq;
using Protobot.Custom;
using Protobot.Geometry;
using UnityEngine;

namespace Protobot.Polycarb {
    // V2: makes a polycarbonate piece from a sketch's chosen outline: the sheet's thickness extruded (sketch X
    // and Y become the part's X and Z, the thickness is along Y), with a hole collider in each round or square
    // hole so screws and shafts go through it like any VEX part's holes.
    public static class PolyPart {
        public const float MaxThickness = 0.070f;
        public static readonly (string label, float inches)[] Thicknesses = {
            ("1/32\" (0.031\")", 1f / 32f), ("0.040\"", 0.040f), ("1/16\" (0.0625\")", 1f / 16f), ("0.070\" (most allowed)", 0.070f),
        };

        public static CustomPartData Make(string name, SketchData sketch, List<List<Vector2>> loops, float thickness, string key = null) {
            var outers = loops.Where(l => Regions.SignedArea(l) > 0f).ToList();
            var holeLoops = loops.Where(l => Regions.SignedArea(l) <= 0f).ToList();
            if (outers.Count == 0) return null;

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var half = thickness / 2f;
            foreach (var outer in outers) {
                var holes = holeLoops.Where(h => MeshSlicer.Contains(outer, h[0])).ToList();
                Cap(outer, holes, half, true, vertices, triangles);
                Cap(outer, holes, -half, false, vertices, triangles);
                Walls(outer, half, vertices, triangles);
                foreach (var hole in holes) Walls(hole, half, vertices, triangles);
            }
            // Centred on its outline.
            var bounds = Bounds2D(outers);
            var center = new Vector3(bounds.center.x, 0f, bounds.center.y);
            for (var i = 0; i < vertices.Count; i++) vertices[i] -= center;

            var part = new CustomPartData {
                key = key ?? CustomParts.NewKey(), kind = CustomParts.Polycarbonate, name = name, material = "Polycarbonate",
                thickness = thickness, sketch = JsonUtility.ToJson(sketch), created = DateTime.Now.Ticks,
                holes = FindHoles(sketch, outers, holeLoops, thickness).Select(h => { h.center -= center; return h; }).ToArray(),
            };
            var area = outers.Sum(o => Regions.SignedArea(o)) + holeLoops.Sum(h => Regions.SignedArea(h));
            part.weightGrams = Mathf.Round(area * thickness * 16.387064f * CustomParts.Density("Polycarbonate") * 100f) / 100f;
            CustomParts.SetMesh(part, vertices, triangles);
            return part;
        }

        private static Rect Bounds2D(List<List<Vector2>> loops) {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var p in loops.SelectMany(l => l)) {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static Vector3 To3(Vector2 p, float y) => new(p.x, y, p.y);

        // A flat face: the outline with its holes, triangulated, facing up (top) or down.
        private static void Cap(List<Vector2> outer, List<List<Vector2>> holes, float y, bool up, List<Vector3> vertices, List<int> triangles) {
            var xy = new List<double>();
            var holeStarts = new List<int>();
            var points = new List<Vector2>();
            foreach (var p in outer) { xy.Add(p.x); xy.Add(p.y); points.Add(p); }
            foreach (var hole in holes) {
                holeStarts.Add(points.Count);
                foreach (var p in hole) { xy.Add(p.x); xy.Add(p.y); points.Add(p); }
            }
            var indices = Earcut.Triangulate(xy, holeStarts);
            var start = vertices.Count;
            foreach (var p in points) vertices.Add(To3(p, y));
            for (var i = 0; i + 2 < indices.Count; i += 3)
                Triangle(vertices, triangles, start + indices[i], start + indices[i + 1], start + indices[i + 2], up ? Vector3.up : Vector3.down);
        }

        // The cut edge all around a loop, facing out of the material.
        private static void Walls(List<Vector2> loop, float half, List<Vector3> vertices, List<int> triangles) {
            for (var i = 0; i < loop.Count; i++) {
                var a = loop[i];
                var b = loop[(i + 1) % loop.Count];
                var d = b - a;
                // Material is on the left of the outline (counter-clockwise outside, clockwise holes), so out of it
                // is to the right.
                var outward = new Vector3(d.y, 0f, -d.x).normalized;
                var start = vertices.Count;
                vertices.Add(To3(a, half));
                vertices.Add(To3(b, half));
                vertices.Add(To3(b, -half));
                vertices.Add(To3(a, -half));
                Triangle(vertices, triangles, start, start + 1, start + 2, outward);
                Triangle(vertices, triangles, start, start + 2, start + 3, outward);
            }
        }

        // Adds a triangle wound so it faces <paramref name="facing"/>.
        private static void Triangle(List<Vector3> vertices, List<int> triangles, int a, int b, int c, Vector3 facing) {
            var normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            if (Vector3.Dot(normal, facing) < 0f) (b, c) = (c, b);
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        // Circles and squares of hole size that ended up as holes in the piece.
        private static IEnumerable<CustomHole> FindHoles(SketchData sketch, List<List<Vector2>> outers, List<List<Vector2>> holeLoops, float thickness) {
            bool IsHole(Vector2 point) =>
                outers.Any(o => MeshSlicer.Contains(o, point)) && holeLoops.Any(h => MeshSlicer.Contains(h, point));
            foreach (var e in sketch.entities.Where(e => !e.construction)) {
                if (e.kind == EntityKind.Circle && e.radius * 2f >= 0.1f && e.radius * 2f <= 1.1f) {
                    var c = sketch.P(e.a);
                    if (!IsHole(c)) continue;
                    var d = e.radius * 2f;
                    yield return new CustomHole {
                        center = new Vector3(c.x, 0f, c.y), axis = Vector3.up, size = d, depth = thickness, square = false,
                    };
                }
                else if (e.kind == EntityKind.Rectangle) {
                    var a = sketch.P(e.a);
                    var c = sketch.P(e.c);
                    var w = Mathf.Abs(c.x - a.x);
                    var h = Mathf.Abs(c.y - a.y);
                    var mid = (a + c) / 2f;
                    // A square VEX-size hole (for square shafts and inserts).
                    if (Mathf.Abs(w - h) < 0.01f && w >= 0.1f && w <= 0.5f && IsHole(mid))
                        yield return new CustomHole {
                            center = new Vector3(mid.x, 0f, mid.y), axis = Vector3.up, size = w, depth = thickness, square = true,
                        };
                }
            }
        }

        /// <summary>The piece's outline in its own top view (x, z), for laying pieces on a sheet: its top face.</summary>
        public static List<Vector2> TopOutline(CustomPartData part, out List<Vector2> points, out List<int> top) {
            points = new List<Vector2>();
            top = new List<int>();
            if (!CustomParts.ReadMesh(part, out var vertices, out var triangles)) return new List<Vector2>();
            var index = new Dictionary<int, int>();
            for (var t = 0; t + 2 < triangles.Length; t += 3) {
                var a = vertices[triangles[t]];
                var b = vertices[triangles[t + 1]];
                var c = vertices[triangles[t + 2]];
                if (a.y < 0f || b.y < 0f || c.y < 0f) continue;
                if (Vector3.Cross(b - a, c - a).y <= 0f) continue;
                for (var k = 0; k < 3; k++) {
                    var vi = triangles[t + k];
                    if (!index.TryGetValue(vi, out var id)) {
                        id = points.Count;
                        index[vi] = id;
                        points.Add(new Vector2(vertices[vi].x, vertices[vi].z));
                    }
                    top.Add(id);
                }
            }
            return Hull(points);
        }

        /// <summary>Convex hull (Andrew's monotone chain), counter-clockwise.</summary>
        public static List<Vector2> Hull(List<Vector2> input) {
            var points = input.Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToList();
            if (points.Count < 3) return points;
            var hull = new List<Vector2>();
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            foreach (var p in points) {
                while (hull.Count >= 2 && Cross(hull[^2], hull[^1], p) <= 0f) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            var lower = hull.Count + 1;
            for (var i = points.Count - 2; i >= 0; i--) {
                var p = points[i];
                while (hull.Count >= lower && Cross(hull[^2], hull[^1], p) <= 0f) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        /// <summary>The smallest rectangle around a hull (any angle): its size and the turn that squares it up.</summary>
        public static (Vector2 size, float angle) SmallestBox(List<Vector2> hull) {
            if (hull.Count < 2) return (Vector2.zero, 0f);
            var best = (size: Vector2.positiveInfinity, angle: 0f);
            var bestArea = float.MaxValue;
            for (var i = 0; i < hull.Count; i++) {
                var edge = hull[(i + 1) % hull.Count] - hull[i];
                if (edge.sqrMagnitude < 1e-10f) continue;
                var angle = -Mathf.Atan2(edge.y, edge.x);
                var size = BoxAt(hull, angle);
                var area = size.x * size.y;
                if (area < bestArea - 1e-6f) {
                    bestArea = area;
                    best = (size, angle);
                }
            }
            return best;
        }

        public static Vector2 BoxAt(List<Vector2> hull, float angle) {
            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var p in hull) {
                var r = new Vector2(p.x * cos - p.y * sin, p.x * sin + p.y * cos);
                min = Vector2.Min(min, r);
                max = Vector2.Max(max, r);
            }
            return max - min;
        }
    }
}
