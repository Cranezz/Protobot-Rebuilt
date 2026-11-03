using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Interface {
    // V2: a 2D drawing surface inside a UI rect (the sketch editor, the plastic sheet layout): filled triangles,
    // lines and dots in the rect's own local units, redrawn whenever its owner calls Clear() and draws again.
    public class ShapeGraphic : MaskableGraphic {
        private readonly List<UIVertex> vertices = new();
        private readonly List<int> indices = new();

        /// <summary>A surface filling <paramref name="parent"/> (its CanvasRenderer added first, as UI needs).</summary>
        public static ShapeGraphic Create(Transform parent) {
            var rect = Ui.Rect("Shapes", parent).Fill();
            rect.gameObject.AddComponent<CanvasRenderer>();
            var graphic = rect.gameObject.AddComponent<ShapeGraphic>();
            graphic.raycastTarget = false;
            return graphic;
        }

        public void Clear() {
            vertices.Clear();
            indices.Clear();
            SetVerticesDirty();
        }

        private int Vertex(Vector2 p, Color color) {
            vertices.Add(new UIVertex { position = p, color = color, uv0 = Vector2.zero });
            return vertices.Count - 1;
        }

        public void Triangle(Vector2 a, Vector2 b, Vector2 c, Color color) {
            var i = Vertex(a, color);
            Vertex(b, color);
            Vertex(c, color);
            indices.Add(i);
            indices.Add(i + 1);
            indices.Add(i + 2);
            SetVerticesDirty();
        }

        public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color) {
            Triangle(a, b, c, color);
            Triangle(a, c, d, color);
        }

        public void Line(Vector2 a, Vector2 b, Color color, float width = 1.5f) {
            var dir = b - a;
            if (dir.sqrMagnitude < 1e-8f) return;
            var n = new Vector2(-dir.y, dir.x).normalized * (width / 2f);
            Quad(a - n, a + n, b + n, b - n, color);
        }

        public void Dashed(Vector2 a, Vector2 b, Color color, float width = 1f, float dash = 6f, float gap = 4f) {
            var length = Vector2.Distance(a, b);
            if (length < 1e-4f) return;
            var dir = (b - a) / length;
            for (var t = 0f; t < length; t += dash + gap)
                Line(a + dir * t, a + dir * Mathf.Min(t + dash, length), color, width);
        }

        public void Polyline(IList<Vector2> points, Color color, float width = 1.5f, bool closed = false) {
            for (var i = 0; i + 1 < points.Count; i++) Line(points[i], points[i + 1], color, width);
            if (closed && points.Count > 2) Line(points[^1], points[0], color, width);
        }

        public void Disc(Vector2 center, float radius, Color color, int segments = 16) {
            for (var i = 0; i < segments; i++) {
                var a0 = i * Mathf.PI * 2f / segments;
                var a1 = (i + 1) * Mathf.PI * 2f / segments;
                Triangle(center, center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius,
                         center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius, color);
            }
        }

        public void Ring(Vector2 center, float radius, Color color, float width = 1.5f, int segments = 32) {
            var points = new List<Vector2>();
            for (var i = 0; i < segments; i++) {
                var a = i * Mathf.PI * 2f / segments;
                points.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            }
            Polyline(points, color, width, true);
        }

        public void Rect(Vector2 min, Vector2 max, Color color) =>
            Quad(min, new Vector2(min.x, max.y), max, new Vector2(max.x, min.y), color);

        /// <summary>Filled triangles from a triangulation (points and index triples).</summary>
        public void Triangles(IList<Vector2> points, IList<int> triangles, Color color) {
            for (var i = 0; i + 2 < triangles.Count; i += 3)
                Triangle(points[triangles[i]], points[triangles[i + 1]], points[triangles[i + 2]], color);
        }

        protected override void OnPopulateMesh(VertexHelper vh) {
            vh.Clear();
            foreach (var v in vertices) vh.AddVert(v);
            for (var i = 0; i + 2 < indices.Count; i += 3)
                vh.AddTriangle(indices[i], indices[i + 1], indices[i + 2]);
        }
    }
}
