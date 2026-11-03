using System.Collections.Generic;
using Protobot.Interface;
using UnityEngine;

namespace Protobot.Polycarb {
    // V2: the sheet editor's tool icons, drawn as shapes (like a CAD program's sketch toolbar): you see what a
    // tool does without reading its name. Each draws in a 24 x 24 box around (0, 0).
    public static class SketchIcons {
        public static void Draw(ShapeGraphic graphic, string icon, Color c) {
            graphic.Clear();
            Draw(new Pen(graphic, Vector2.zero, 1f), icon, c);
        }

        /// <summary>An icon drawn small somewhere on a bigger drawing (the sketch's relation markers).</summary>
        public static void DrawAt(ShapeGraphic graphic, string icon, Color c, Vector2 at, float scale) =>
            Draw(new Pen(graphic, at, scale), icon, c);

        // Draws into a ShapeGraphic moved and scaled.
        public class Pen {
            private readonly ShapeGraphic g;
            private readonly Vector2 at;
            private readonly float k;
            public Pen(ShapeGraphic graphic, Vector2 offset, float scale) { g = graphic; at = offset; k = scale; }
            private Vector2 P(Vector2 p) => at + p * k;
            public void Line(Vector2 a, Vector2 b, Color c, float w) => g.Line(P(a), P(b), c, w * Mathf.Max(0.6f, k));
            public void Dashed(Vector2 a, Vector2 b, Color c, float w, float dash, float gap) => g.Dashed(P(a), P(b), c, w * Mathf.Max(0.6f, k), dash * k, gap * k);
            public void Polyline(IList<Vector2> points, Color c, float w, bool closed = false) {
                var list = new List<Vector2>();
                foreach (var p in points) list.Add(P(p));
                g.Polyline(list, c, w * Mathf.Max(0.6f, k), closed);
            }
            public void Ring(Vector2 center, float r, Color c, float w, int segments) => g.Ring(P(center), r * k, c, w * Mathf.Max(0.6f, k), segments);
            public void Disc(Vector2 center, float r, Color c, int segments) => g.Disc(P(center), r * k, c, segments);
            public void Rect(Vector2 min, Vector2 max, Color c) => g.Rect(P(min), P(max), c);
            public void Triangle(Vector2 a, Vector2 b, Vector2 d, Color c) => g.Triangle(P(a), P(b), P(d), c);
            public void Triangles(List<Vector2> points, List<int> indices, Color c) {
                var list = new List<Vector2>();
                foreach (var p in points) list.Add(P(p));
                g.Triangles(list, indices, c);
            }
        }

        private static void Draw(Pen g, string icon, Color c) {
            var faint = new Color(c.r, c.g, c.b, 0.45f);
            switch (icon) {
                case "Select":
                    g.Triangles(new List<Vector2> { new(-6, 10), new(-6, -6), new(-2, -2), new(1, -9), new(4, -8), new(1, -1), new(6, -1) },
                        new List<int> { 0, 1, 2, 0, 2, 6, 2, 3, 4, 2, 4, 5, 2, 5, 6 }, c);
                    break;
                case "Line":
                    g.Line(new Vector2(-8, -8), new Vector2(8, 8), c, 2f);
                    Dot(g, new Vector2(-8, -8), c);
                    Dot(g, new Vector2(8, 8), c);
                    break;
                case "Rectangle":
                    g.Polyline(new[] { new Vector2(-9, -6), new Vector2(9, -6), new Vector2(9, 6), new Vector2(-9, 6) }, c, 2f, true);
                    Dot(g, new Vector2(-9, -6), c);
                    Dot(g, new Vector2(9, 6), c);
                    break;
                case "Circle":
                    g.Ring(Vector2.zero, 9f, c, 2f, 32);
                    g.Line(new Vector2(-3, 0), new Vector2(3, 0), faint, 1f);
                    g.Line(new Vector2(0, -3), new Vector2(0, 3), faint, 1f);
                    break;
                case "Arc": {
                    var points = new List<Vector2>();
                    for (var i = 0; i <= 16; i++) {
                        var a = Mathf.PI * (0.1f + 0.8f * i / 16f);
                        points.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 10f + new Vector2(0, -5));
                    }
                    g.Polyline(points, c, 2f);
                    Dot(g, new Vector2(0, -5), faint);
                    break;
                }
                case "Slot":
                    g.Polyline(Slot(new Vector2(-6, 0), new Vector2(6, 0), 5f), c, 2f, true);
                    break;
                case "Trim":
                    g.Line(new Vector2(-10, 0), new Vector2(10, 0), c, 2f);
                    g.Line(new Vector2(-4, -9), new Vector2(-4, 9), faint, 1.5f);
                    g.Line(new Vector2(5, -9), new Vector2(5, 9), faint, 1.5f);
                    g.Line(new Vector2(-2, 3), new Vector2(3, -3), Theme.Danger, 2f);
                    g.Line(new Vector2(-2, -3), new Vector2(3, 3), Theme.Danger, 2f);
                    break;
                case "Construction":
                    g.Dashed(new Vector2(-10, -6), new Vector2(10, 6), c, 2f, 4f, 3f);
                    break;
                case "Dimension":
                    g.Line(new Vector2(-10, -7), new Vector2(-10, 7), faint, 1.5f);
                    g.Line(new Vector2(10, -7), new Vector2(10, 7), faint, 1.5f);
                    g.Line(new Vector2(-9, 0), new Vector2(9, 0), c, 1.5f);
                    g.Triangle(new Vector2(-9, 0), new Vector2(-4, 3), new Vector2(-4, -3), c);
                    g.Triangle(new Vector2(9, 0), new Vector2(4, 3), new Vector2(4, -3), c);
                    break;
                case "Horizontal":
                    g.Line(new Vector2(-10, 0), new Vector2(10, 0), c, 2.5f);
                    Dot(g, new Vector2(-10, 0), c);
                    Dot(g, new Vector2(10, 0), c);
                    break;
                case "Vertical":
                    g.Line(new Vector2(0, -10), new Vector2(0, 10), c, 2.5f);
                    Dot(g, new Vector2(0, -10), c);
                    Dot(g, new Vector2(0, 10), c);
                    break;
                case "Parallel":
                    g.Line(new Vector2(-9, -4), new Vector2(1, 10), c, 2f);
                    g.Line(new Vector2(-1, -10), new Vector2(9, 4), c, 2f);
                    break;
                case "Perpendicular":
                    g.Line(new Vector2(-9, -8), new Vector2(9, -8), c, 2f);
                    g.Line(new Vector2(0, -8), new Vector2(0, 9), c, 2f);
                    g.Polyline(new[] { new Vector2(0, -3), new Vector2(5, -3), new Vector2(5, -8) }, faint, 1f);
                    break;
                case "Equal":
                    g.Line(new Vector2(-9, 4), new Vector2(9, 4), c, 2.5f);
                    g.Line(new Vector2(-9, -4), new Vector2(9, -4), c, 2.5f);
                    break;
                case "Coincident":
                    g.Line(new Vector2(-10, -6), new Vector2(10, 6), faint, 1.5f);
                    g.Disc(Vector2.zero, 4f, c, 16);
                    break;
                case "Tangent":
                    g.Ring(new Vector2(0, -3), 7f, c, 2f, 28);
                    g.Line(new Vector2(-11, 4), new Vector2(11, 4), c, 2f);
                    break;
                case "Concentric":
                    g.Ring(Vector2.zero, 10f, c, 1.8f, 32);
                    g.Ring(Vector2.zero, 5f, c, 1.8f, 24);
                    break;
                case "Midpoint":
                    g.Line(new Vector2(-10, 0), new Vector2(10, 0), faint, 1.5f);
                    g.Disc(Vector2.zero, 3.5f, c, 14);
                    g.Line(new Vector2(-10, -4), new Vector2(-10, 4), c, 1.5f);
                    g.Line(new Vector2(10, -4), new Vector2(10, 4), c, 1.5f);
                    break;
                case "Fix":
                    g.Disc(new Vector2(0, 4), 3.5f, c, 14);
                    g.Line(new Vector2(0, 1), new Vector2(0, -5), c, 2f);
                    g.Line(new Vector2(-8, -6), new Vector2(8, -6), c, 2f);
                    for (var x = -7; x <= 7; x += 4) g.Line(new Vector2(x, -6), new Vector2(x - 3, -10), faint, 1.2f);
                    break;
                case "Delete":
                    g.Line(new Vector2(-7, -7), new Vector2(7, 7), c, 2.2f);
                    g.Line(new Vector2(-7, 7), new Vector2(7, -7), c, 2.2f);
                    break;
                case "Fit":
                    foreach (var sx in new[] { -1f, 1f })
                        foreach (var sy in new[] { -1f, 1f })
                            g.Polyline(new[] { new Vector2(sx * 10, sy * 4), new Vector2(sx * 10, sy * 10), new Vector2(sx * 4, sy * 10) }, c, 2f);
                    break;
                case "Hole":
                    g.Ring(Vector2.zero, 8f, Theme.Accent, 2f, 28);
                    g.Disc(Vector2.zero, 2.5f, Theme.Accent, 12);
                    break;
            }
        }

        private static void Dot(Pen g, Vector2 p, Color c) => g.Rect(p - Vector2.one * 2f, p + Vector2.one * 2f, c);

        /// <summary>A rounded slot outline from one end's centre to the other's.</summary>
        public static List<Vector2> Slot(Vector2 a, Vector2 b, float radius, int arcSegments = 12) {
            var d = (b - a).normalized;
            var n = new Vector2(-d.y, d.x);
            var start = Mathf.Atan2(n.y, n.x);
            var points = new List<Vector2>();
            for (var i = 0; i <= arcSegments; i++) {
                var angle = start - Mathf.PI * i / arcSegments;
                points.Add(b + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
            for (var i = 0; i <= arcSegments; i++) {
                var angle = start + Mathf.PI - Mathf.PI * i / arcSegments;
                points.Add(a + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
            return points;
        }
    }
}
