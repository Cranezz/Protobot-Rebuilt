using System;
using System.Collections.Generic;
using System.Linq;
using Protobot.Custom;
using Protobot.Geometry;
using Protobot.Interface;
using UnityEngine;

namespace Protobot.Polycarb {
    // V2: makes a polycarbonate piece from a sketch's chosen outline: the sheet's thickness extruded (sketch X
    // and Y become the part's X and Z, the thickness is along Y), with a hole collider in every cut-out of screw
    // size (its exact shape: a slot or a big hole lets the screw slide in it) and on circles and arcs given a hole
    // by hand, so screws and shafts go through it like any VEX part's holes. It's cut from the plastic chosen for
    // it (PlasticMaterials: its color, finish and weight).
    public static class PolyPart {
        public const float MaxThickness = 0.070f;
        public static readonly (string label, float inches)[] Thicknesses = {
            ("1/32\" (0.031\")", 1f / 32f), ("0.040\"", 0.040f), ("1/16\" (0.0625\")", 1f / 16f), ("0.070\" (most allowed)", 0.070f),
        };

        public static CustomPartData Make(string name, SketchData sketch, List<List<Vector2>> loops, float thickness, string key = null, string finish = null) {
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

            var plastic = PlasticMaterials.Find(finish ?? PlasticMaterials.Default);
            var part = new CustomPartData {
                key = key ?? CustomParts.NewKey(), kind = CustomParts.Polycarbonate, name = name, material = plastic.plastic, finish = plastic.id,
                thickness = thickness, sketch = JsonUtility.ToJson(sketch), created = DateTime.Now.Ticks,
                holes = FindHoles(sketch, outers, holeLoops, thickness).Select(h => { h.center -= center; return h; }).ToArray(),
            };
            var area = outers.Sum(o => Regions.SignedArea(o)) + holeLoops.Sum(h => Regions.SignedArea(h));
            part.weightGrams = Mathf.Round(area * thickness * 16.387064f * CustomParts.Density(plastic.plastic) * 100f) / 100f;
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

        /// <summary>Biggest cut-out still taken for a screw hole (bigger ones are lightening holes).</summary>
        public const float MaxHoleSize = 1.25f;

        /// <summary>
        /// V2: the piece's screw holes, found as it is: every cut-out inside it up to <see cref="MaxHoleSize"/>
        /// across becomes a hole of its exact shape (round ones round), unless it was marked as not a hole; and the
        /// holes placed by hand on circles and arcs (half a circle at the edge still takes a screw). In sketch
        /// coordinates (x, 0, y).
        /// </summary>
        public static List<CustomHole> FindHoles(SketchData sketch, List<List<Vector2>> outers, List<List<Vector2>> holeLoops, float thickness) {
            var found = new List<CustomHole>();
            foreach (var loop in holeLoops) {
                if (!outers.Any(o => MeshSlicer.Contains(o, loop[0]))) continue;
                if (sketch.notHoles != null && sketch.notHoles.Any(n => MeshSlicer.Contains(loop, n))) continue;
                var fit = FitHole(loop);
                if (!fit.valid || Mathf.Max(fit.size, fit.size2) > MaxHoleSize) continue;
                var center = new Vector3(fit.center.x, 0f, fit.center.y);
                var hole = new CustomHole {
                    center = center, axis = Vector3.up, size = fit.size, size2 = fit.size2, depth = thickness, square = !fit.round,
                    up = fit.round ? Vector3.zero : new Vector3(fit.side.x, 0f, fit.side.y).normalized,
                };
                if (!fit.round) {
                    // Its exact shape, along the hole's own sides (right = up x axis, as its collider is turned).
                    var right = Vector3.Cross(hole.up, Vector3.up).normalized;
                    hole.outline = loop.Select(q => {
                        var d = new Vector3(q.x, 0f, q.y) - center;
                        return new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, hole.up));
                    }).ToArray();
                }
                found.Add(hole);
            }
            foreach (var manual in sketch.manualHoles ?? new List<ManualHole>()) {
                var center = new Vector3(manual.center.x, 0f, manual.center.y);
                if (found.Any(f => (f.center - center).sqrMagnitude < 1e-4f)) continue;
                found.Add(new CustomHole {
                    center = center, axis = Vector3.up, size = manual.radius * 2f, depth = thickness, square = false,
                });
            }
            return found;
        }

        public struct HoleFit {
            public bool valid, round;
            public Vector2 center, side;   // side: along size2 (a slot's long or short side)
            public float size, size2;      // size2 = 0: square (or round)

            public string Describe() =>
                round ? Inch.Format(size) + " round"
                : size2 > 0f ? Inch.Format(Mathf.Min(size, size2)) + " × " + Inch.Format(Mathf.Max(size, size2)) + " slot"
                : Inch.Format(size) + " square";

            /// <summary>Whether an 8-32 screw has room to slide in it.</summary>
            public bool Slides => !round && Mathf.Max(size, size2) > 0.164f + 0.04f || round && size > 0.164f + 0.04f;
        }

        /// <summary>
        /// The hole for an outline: round when it's a circle (its area's diameter), otherwise the smallest box
        /// around it at any angle (a square, or a slot a screw slides along).
        /// </summary>
        public static HoleFit FitHole(List<Vector2> outline) {
            var hull = Hull(outline);
            if (hull.Count < 3) return default;
            var (box, angle) = SmallestBox(hull);
            if (box.x < 0.02f || box.y < 0.02f) return default;
            var area = Mathf.Abs(Regions.SignedArea(outline));
            // The box's middle and second side, back in the outline's space (BoxAt turns points by the angle).
            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var p in hull) {
                var r = new Vector2(p.x * cos - p.y * sin, p.x * sin + p.y * cos);
                min = Vector2.Min(min, r);
                max = Vector2.Max(max, r);
            }
            var mid = (min + max) / 2f;
            var fit = new HoleFit {
                valid = true,
                center = new Vector2(mid.x * cos + mid.y * sin, -mid.x * sin + mid.y * cos),
                side = new Vector2(sin, cos),
            };
            var fill = area / (box.x * box.y);
            fit.round = Mathf.Max(box.x, box.y) < Mathf.Min(box.x, box.y) * 1.1f && fill > 0.72f && fill < 0.83f;
            if (fit.round) {
                fit.size = Mathf.Sqrt(4f * area / Mathf.PI);
                fit.side = Vector2.zero;
            }
            else {
                fit.size = box.x;
                fit.size2 = Mathf.Abs(box.x - box.y) < 0.005f ? 0f : box.y;
            }
            return fit;
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
