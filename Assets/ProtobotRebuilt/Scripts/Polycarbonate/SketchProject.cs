using System;
using System.Collections.Generic;
using System.Linq;
using Protobot.Interface;
using UnityEngine;

namespace Protobot.Polycarb {
    /// <summary>V2: a sketch's plane in the build (sketching on a part's face): its origin and axes. u is the
    /// sketch's x (right on screen looking at it), v its y (up), n out of the face (v = u × n, so a piece made on it
    /// is the part's x, y = n, z = v).</summary>
    public struct SketchPlane {
        public Vector3 origin, u, v, n;
        public Vector2 To2(Vector3 world) => new(Vector3.Dot(world - origin, u), Vector3.Dot(world - origin, v));
        public Vector3 To3(Vector2 p, float height = 0f) => origin + u * p.x + v * p.y + n * height;
        public float Height(Vector3 world) => Vector3.Dot(world - origin, n);
        public Quaternion Rotation => Quaternion.LookRotation(v, n);
    }

    // V2: Convert Entities (SolidWorks), or projecting geometry (Fusion, Inventor): a part's edges and holes put on
    // the sketch plane, to draw from and snap to. Mesh edges are joined where they run on in a straight line and
    // doubles dropped (a plate's front and back project to one outline); holes come in as their own shapes (a round
    // hole as a circle with its centre, a square one as its square), not the many edges of their walls.
    public static class SketchProject {
        public class Shape {
            public readonly List<(Vector2 a, Vector2 b)> segments = new();
            /// <summary>The sides of square and shaped holes (circles are always holes).</summary>
            public readonly List<(Vector2 a, Vector2 b)> holeSides = new();
            public readonly List<(Vector2 c, float r)> circles = new();
            public bool Empty => segments.Count == 0 && holeSides.Count == 0 && circles.Count == 0;
            public int HoleCount => circles.Count + holeSides.Count;

            public void AddRange(Shape other) {
                segments.AddRange(other.segments);
                holeSides.AddRange(other.holeSides);
                circles.AddRange(other.circles);
            }
        }

        /// <summary>A part's edges and holes on the plane; with <paramref name="nearDepth"/>, only those within that
        /// distance of it (in front or behind); with <paramref name="faceHeight"/>, only those lying on the plane at
        /// that height (one face).</summary>
        public static Shape Part(GameObject part, SketchPlane plane, float? nearDepth = null, float? faceHeight = null) {
            var shape = new Shape();
            if (part == null) return shape;
            bool Keep(Vector3 w, float halfDepth = 0f) {
                var h = plane.Height(w);
                if (faceHeight.HasValue) return Mathf.Abs(h - faceHeight.Value) < 0.01f + halfDepth;
                return !nearDepth.HasValue || Mathf.Abs(h) <= nearDepth.Value + halfDepth;
            }
            var holes = new List<(Vector2 c, float r)>();
            foreach (var hole in part.GetComponentsInChildren<HoleCollider>()) {
                // A hole runs through the wall: its middle is inside the metal, so it counts while it reaches the face.
                var halfDepth = Mathf.Abs(hole.transform.lossyScale.z) / 2f;
                if (!Keep(hole.transform.position, halfDepth)) continue;
                var before = shape.HoleCount;
                AddHole(shape, hole, plane);
                if (shape.HoleCount > before) {
                    var t = hole.transform;
                    holes.Add((plane.To2(t.position), Mathf.Max(Mathf.Abs(t.lossyScale.x), Mathf.Abs(t.lossyScale.y)) * 0.75f));
                }
            }
            var raw = new List<(Vector2, Vector2)>();
            foreach (var (a, b) in FeaturePicker.SharpEdges(part)) {
                if (!Keep(a) || !Keep(b)) continue;
                var pa = plane.To2(a);
                var pb = plane.To2(b);
                if (Vector2.Distance(pa, pb) < 1e-3f) continue;   // along the view: a point
                // A hole's own wall edges: the hole's shape stands for them.
                if (holes.Any(h => Vector2.Distance(pa, h.c) < h.r && Vector2.Distance(pb, h.c) < h.r)) continue;
                raw.Add((pa, pb));
            }
            shape.segments.AddRange(Merge(raw));
            return shape;
        }

        /// <summary>One edge on the plane.</summary>
        public static Shape Edge(Vector3 a, Vector3 b, SketchPlane plane) {
            var shape = new Shape();
            var pa = plane.To2(a);
            var pb = plane.To2(b);
            if (Vector2.Distance(pa, pb) > 1e-3f) shape.segments.Add((pa, pb));
            return shape;
        }

        /// <summary>A hole on the plane: round as a circle, square as its square, any other as its outline.</summary>
        public static Shape Hole(HoleCollider hole, SketchPlane plane) {
            var shape = new Shape();
            AddHole(shape, hole, plane);
            return shape;
        }

        private static void AddHole(Shape shape, HoleCollider hole, SketchPlane plane) {
            var t = hole.transform;
            if (Mathf.Abs(Vector3.Dot(t.forward, plane.n)) < 0.9f) return;  // a hole across the view
            var center = plane.To2(t.position);
            var w = Mathf.Abs(t.lossyScale.x);
            var h = Mathf.Abs(t.lossyScale.y);
            var data = hole.holeData;
            var outline = data?.outline;
            var square = data != null && HoleShapes.instance != null && data.shape == HoleShapes.instance.GetShapeMesh("square");
            Vector2 Corner(float x, float y) => plane.To2(t.position + t.right * x + t.up * y);
            if (outline != null && outline.Length >= 3) {
                for (var i = 0; i < outline.Length; i++)
                    shape.holeSides.Add((Corner(outline[i].x, outline[i].y), Corner(outline[(i + 1) % outline.Length].x, outline[(i + 1) % outline.Length].y)));
            }
            else if (square || Mathf.Abs(w - h) > 1e-3f) {
                var corners = new[] { Corner(-w / 2f, -h / 2f), Corner(w / 2f, -h / 2f), Corner(w / 2f, h / 2f), Corner(-w / 2f, h / 2f) };
                for (var i = 0; i < 4; i++) shape.holeSides.Add((corners[i], corners[(i + 1) % 4]));
            }
            else shape.circles.Add((center, w / 2f));
        }

        // Pieces joined where one runs straight on into another, and doubles dropped.
        public static List<(Vector2 a, Vector2 b)> Merge(List<(Vector2 a, Vector2 b)> segments) {
            Vector2Int Key(Vector2 p) => new(Mathf.RoundToInt(p.x * 2000f), Mathf.RoundToInt(p.y * 2000f));
            var unique = new Dictionary<(Vector2Int, Vector2Int), (Vector2, Vector2)>();
            foreach (var (a, b) in segments) {
                var ka = Key(a);
                var kb = Key(b);
                if (ka == kb) continue;
                var key = ka.x < kb.x || ka.x == kb.x && ka.y < kb.y ? (ka, kb) : (kb, ka);
                unique[key] = (a, b);
            }
            var list = unique.Values.ToList();
            var changed = true;
            var guard = 0;
            while (changed && guard++ < 2000) {
                changed = false;
                var at = new Dictionary<Vector2Int, List<int>>();
                for (var i = 0; i < list.Count; i++) {
                    foreach (var k in new[] { Key(list[i].Item1), Key(list[i].Item2) }) {
                        if (!at.TryGetValue(k, out var l)) at[k] = l = new List<int>();
                        l.Add(i);
                    }
                }
                foreach (var pair in at) {
                    if (pair.Value.Count != 2) continue;
                    var i = pair.Value[0];
                    var j = pair.Value[1];
                    var (a1, b1) = list[i];
                    var (a2, b2) = list[j];
                    var d1 = (b1 - a1).normalized;
                    var d2 = (b2 - a2).normalized;
                    if (Mathf.Abs(d1.x * d2.y - d1.y * d2.x) > 0.004f) continue;
                    // The two far ends.
                    var shared = pair.Key;
                    var far1 = Key(a1) == shared ? b1 : a1;
                    var far2 = Key(a2) == shared ? b2 : a2;
                    list[i] = (far1, far2);
                    list.RemoveAt(j);
                    changed = true;
                    break;
                }
            }
            return list;
        }

        /// <summary>Adds the shape to the sketch as converted entities: purple, fixed where they are; edges as
        /// construction (reference) or cut lines as asked, holes as cut lines (holes in the piece made over them) unless
        /// <paramref name="holesAsReference"/>; what's already there isn't added twice. Returns how many were added.</summary>
        public static int Add(SketchData sketch, Shape shape, bool construction, bool holesAsReference = false) {
            var added = 0;
            var projectedPoints = new HashSet<int>(sketch.entities.Where(e => e.projected).SelectMany(sketch.PointsOf));
            int PointAt(Vector2 p) {
                foreach (var id in projectedPoints)
                    if (Vector2.Distance(sketch.P(id), p) < 1e-4f) return id;
                var made = sketch.AddPoint(p).id;
                projectedPoints.Add(made);
                return made;
            }
            foreach (var (a, b, hole) in shape.segments.Select(x => (x.a, x.b, false)).Concat(shape.holeSides.Select(x => (x.a, x.b, true)))) {
                if (sketch.entities.Any(e => e.projected && e.kind == EntityKind.Line &&
                    (Vector2.Distance(sketch.P(e.a), a) < 1e-4f && Vector2.Distance(sketch.P(e.b), b) < 1e-4f ||
                     Vector2.Distance(sketch.P(e.a), b) < 1e-4f && Vector2.Distance(sketch.P(e.b), a) < 1e-4f))) continue;
                var line = sketch.Add(EntityKind.Line, PointAt(a), PointAt(b));
                line.projected = true;
                line.construction = hole ? holesAsReference : construction;
                sketch.Relate(RelationKind.Fix, line.id);
                added++;
            }
            foreach (var (c, r) in shape.circles) {
                if (sketch.entities.Any(e => e.projected && e.kind == EntityKind.Circle && Vector2.Distance(sketch.P(e.a), c) < 1e-4f && Mathf.Abs(e.radius - r) < 1e-4f)) continue;
                var circle = sketch.Add(EntityKind.Circle, PointAt(c), radius: r);
                circle.projected = true;
                circle.construction = holesAsReference;
                sketch.Relate(RelationKind.Fix, circle.id);
                added++;
            }
            return added;
        }
    }
}
