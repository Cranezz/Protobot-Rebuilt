using System.Collections.Generic;
using Protobot.Geometry;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.RobotCode.Blocks {
    /// <summary>V2 (Blocks): draws a block's outline (Scratch-style: the notch on top, the tab under it, a hat's curved
    /// top, C mouths, round reporters, pointed booleans) and the backgrounds of its slots, in the rect's local space
    /// (top-left origin, y down in the shapes, made negative when drawn).</summary>
    public class BlockGraphic : MaskableGraphic {
        public const float NotchX = 12f, NotchW = 16f, NotchD = 4f, Radius = 4f, HatBump = 14f;

        private struct Shape {
            public List<Vector2> points;
            public Color fill, edge;
            public float edgeWidth;
        }

        private readonly List<Shape> shapes = new();

        public void Clear() {
            shapes.Clear();
            SetVerticesDirty();
        }

        public void Add(List<Vector2> points, Color fill, Color edge, float edgeWidth = 1f) {
            shapes.Add(new Shape { points = points, fill = fill, edge = edge, edgeWidth = edgeWidth });
            SetVerticesDirty();
        }

        // Raycasts: only on the block's own shape (not the empty part of its rect beside a mouth).
        public override bool Raycast(Vector2 sp, Camera eventCamera) {
            if (!base.Raycast(sp, eventCamera)) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, sp, eventCamera, out var local)) return false;
            var p = new Vector2(local.x - rectTransform.rect.xMin, rectTransform.rect.yMax - local.y);   // top-left origin, y down
            foreach (var s in shapes) {
                if (s.edgeWidth < 0f) continue;   // slot backgrounds are part of the block anyway
                if (Inside(s.points, p)) return true;
            }
            return false;
        }

        private static bool Inside(List<Vector2> poly, Vector2 p) {
            var inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++) {
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            }
            return inside;
        }

        protected override void OnPopulateMesh(VertexHelper vh) {
            vh.Clear();
            var origin = new Vector2(rectTransform.rect.xMin, rectTransform.rect.yMax);
            foreach (var s in shapes) {
                if (s.points.Count < 3) continue;
                var xy = new List<double>(s.points.Count * 2);
                foreach (var p in s.points) {
                    xy.Add(p.x);
                    xy.Add(p.y);
                }
                var tris = Earcut.Triangulate(xy, null);
                var start = vh.currentVertCount;
                foreach (var p in s.points) vh.AddVert(new Vector3(origin.x + p.x, origin.y - p.y), s.fill, Vector4.zero);
                for (var i = 0; i + 2 < tris.Count; i += 3) vh.AddTriangle(start + tris[i], start + tris[i + 1], start + tris[i + 2]);
                var width = Mathf.Abs(s.edgeWidth);
                if (width <= 0f || s.edge.a <= 0f) continue;
                for (var i = 0; i < s.points.Count; i++) {
                    var a = s.points[i];
                    var b = s.points[(i + 1) % s.points.Count];
                    var d = b - a;
                    if (d.sqrMagnitude < 1e-6f) continue;
                    var n = new Vector2(-d.y, d.x).normalized * (width / 2f);
                    var k = vh.currentVertCount;
                    vh.AddVert(new Vector3(origin.x + a.x - n.x, origin.y - (a.y - n.y)), s.edge, Vector4.zero);
                    vh.AddVert(new Vector3(origin.x + a.x + n.x, origin.y - (a.y + n.y)), s.edge, Vector4.zero);
                    vh.AddVert(new Vector3(origin.x + b.x + n.x, origin.y - (b.y + n.y)), s.edge, Vector4.zero);
                    vh.AddVert(new Vector3(origin.x + b.x - n.x, origin.y - (b.y - n.y)), s.edge, Vector4.zero);
                    vh.AddTriangle(k, k + 1, k + 2);
                    vh.AddTriangle(k, k + 2, k + 3);
                }
            }
        }

        // ------------------------------------------------------------------ outlines (y down)

        private static void Arc(List<Vector2> pts, Vector2 center, float r, float from, float to, int steps = 5) {
            for (var i = 0; i <= steps; i++) {
                var a = Mathf.Deg2Rad * Mathf.Lerp(from, to, i / (float)steps);
                pts.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
        }

        // The top edge from left to right at height y, with the notch at x0 (cut in when notch).
        private static void Top(List<Vector2> pts, float x0, float x1, float y, bool notch) {
            if (notch) {
                pts.Add(new Vector2(x0 + NotchX, y));
                pts.Add(new Vector2(x0 + NotchX + 4f, y + NotchD));
                pts.Add(new Vector2(x0 + NotchX + NotchW - 4f, y + NotchD));
                pts.Add(new Vector2(x0 + NotchX + NotchW, y));
            }
            pts.Add(new Vector2(x1, y));
        }

        // The bottom edge from right to left at height y, with the tab at x0 (hanging under it when tab).
        private static void Bottom(List<Vector2> pts, float x1, float x0, float y, bool tab) {
            pts.Add(new Vector2(x1, y));
            if (tab) {
                pts.Add(new Vector2(x0 + NotchX + NotchW, y));
                pts.Add(new Vector2(x0 + NotchX + NotchW - 4f, y + NotchD));
                pts.Add(new Vector2(x0 + NotchX + 4f, y + NotchD));
                pts.Add(new Vector2(x0 + NotchX, y));
            }
        }

        /// <summary>A stack, hat or cap block (w x h; a hat's h includes its bump).</summary>
        public static List<Vector2> StackShape(float w, float h, bool notch, bool tab, bool hat) {
            var pts = new List<Vector2>();
            var top = hat ? HatBump : 0f;
            if (hat) {
                // A curved cap over the left part.
                var hw = Mathf.Min(80f, w - 8f);
                for (var i = 0; i <= 12; i++) {
                    var t = i / 12f;
                    pts.Add(new Vector2(t * hw, top - Mathf.Sin(t * Mathf.PI) * top));
                }
                pts.Add(new Vector2(w - Radius, top));
            }
            else {
                pts.Add(new Vector2(0f, Radius));
                Arc(pts, new Vector2(Radius, Radius), Radius, 180f, 270f, 3);
                Top(pts, 0f, w - Radius, 0f, notch);
            }
            Arc(pts, new Vector2(w - Radius, top + Radius), Radius, 270f, 360f, 3);
            Arc(pts, new Vector2(w - Radius, h - Radius), Radius, 0f, 90f, 3);
            Bottom(pts, w - Radius, 0f, h, tab);
            Arc(pts, new Vector2(Radius, h - Radius), Radius, 90f, 180f, 3);
            return pts;
        }

        /// <summary>A C block: its top row (height top), mouths (each starts at mouthTops[i], mouthHeights[i] high),
        /// arms between (the else row) and at the bottom (arm high).</summary>
        public static List<Vector2> CShape(float w, float top, float arm, float armWidth, List<float> mouthTops, List<float> mouthHeights,
                                           List<float> armHeights, bool tab) {
            var pts = new List<Vector2>();
            pts.Add(new Vector2(0f, Radius));
            Arc(pts, new Vector2(Radius, Radius), Radius, 180f, 270f, 3);
            Top(pts, 0f, w - Radius, 0f, true);
            Arc(pts, new Vector2(w - Radius, Radius), Radius, 270f, 360f, 3);
            for (var m = 0; m < mouthTops.Count; m++) {
                var mt = mouthTops[m];
                var mb = mt + mouthHeights[m];
                // The arm above this mouth: its right side down, then its underside (with the tab the mouth's first block fits).
                pts.Add(new Vector2(w, mt - Radius));
                Arc(pts, new Vector2(w - Radius, mt - Radius), Radius, 0f, 90f, 2);
                Bottom(pts, w - Radius, armWidth, mt, true);
                pts.Add(new Vector2(armWidth, mt));
                // Down the inside, then the top of the arm below (with the notch the mouth's last block's tab goes in).
                pts.Add(new Vector2(armWidth, mb));
                Top(pts, armWidth, w - Radius, mb, true);
                Arc(pts, new Vector2(w - Radius, mb + Radius), Radius, 270f, 360f, 2);
            }
            var h = mouthTops.Count > 0 ? mouthTops[^1] + mouthHeights[^1] + arm : top;
            Arc(pts, new Vector2(w - Radius, h - Radius), Radius, 0f, 90f, 3);
            Bottom(pts, w - Radius, 0f, h, tab);
            Arc(pts, new Vector2(Radius, h - Radius), Radius, 90f, 180f, 3);
            return pts;
        }

        /// <summary>A round-ended reporter (or a number slot).</summary>
        public static List<Vector2> Pill(float x, float y, float w, float h) {
            var pts = new List<Vector2>();
            var r = Mathf.Min(h / 2f, w / 2f);
            Arc(pts, new Vector2(x + r, y + h - r), r, 90f, 270f, 8);
            Arc(pts, new Vector2(x + w - r, y + r), r, 270f, 450f, 8);
            return pts;
        }

        /// <summary>A pointed boolean (or a boolean slot).</summary>
        public static List<Vector2> Hexagon(float x, float y, float w, float h) {
            var s = Mathf.Min(h / 2f, w / 3f);
            return new List<Vector2> {
                new(x, y + h / 2f), new(x + s, y), new(x + w - s, y), new(x + w, y + h / 2f), new(x + w - s, y + h), new(x + s, y + h),
            };
        }

        /// <summary>A rounded rectangle (a text slot, a dropdown).</summary>
        public static List<Vector2> RoundRect(float x, float y, float w, float h, float r = 4f) {
            var pts = new List<Vector2>();
            r = Mathf.Min(r, Mathf.Min(w, h) / 2f);
            Arc(pts, new Vector2(x + r, y + r), r, 180f, 270f, 3);
            Arc(pts, new Vector2(x + w - r, y + r), r, 270f, 360f, 3);
            Arc(pts, new Vector2(x + w - r, y + h - r), r, 0f, 90f, 3);
            Arc(pts, new Vector2(x + r, y + h - r), r, 90f, 180f, 3);
            return pts;
        }

        /// <summary>A small down-pointing triangle (a dropdown's arrow).</summary>
        public static List<Vector2> Arrow(float cx, float cy, float size) =>
            new() { new(cx - size, cy - size * 0.5f), new(cx + size, cy - size * 0.5f), new(cx, cy + size * 0.6f) };
    }
}
