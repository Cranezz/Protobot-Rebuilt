using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Interface {
    // V2: drawing on top of the 3D view for the saw and measure tools: dimension lines, arrows, rings, dots and
    // value labels, in screen space (always visible, always the same thickness). Tools redraw every frame:
    // Begin(), then lines/labels, and the overlay hides what wasn't drawn. Shapes can be marked as handles: the
    // mouse over one counts as over the interface (so a drag on it doesn't box-select or click the part under it).
    public class Overlay : MaskableGraphic, ICanvasRaycastFilter {
        public static Overlay Instance { get; private set; }

        private struct Quad { public Vector2 a, b, c, d; public Color color; }
        private readonly List<Quad> quads = new();
        private readonly List<(Vector2 a, Vector2 b, float radius)> hitSegments = new();
        private readonly List<(Vector2 center, float radius)> hitDiscs = new();
        // Last frame's handles too: the event system may ask between a tool's Begin() and its drawing.
        private readonly List<(Vector2 a, Vector2 b, float radius)> lastSegments = new();
        private readonly List<(Vector2 center, float radius)> lastDiscs = new();
        private readonly List<Label> labels = new();
        private int labelsUsed;
        private Camera viewCamera;

        public static Camera ViewCamera {
            get {
                if (Instance == null) return Camera.main;
                if (Instance.viewCamera == null) Instance.viewCamera = Camera.main;
                return Instance.viewCamera;
            }
        }

        public static void Create(RectTransform root) {
            var rect = Ui.Rect("Overlay", root).Fill();
            rect.SetAsFirstSibling();  // under the panels
            rect.gameObject.AddComponent<CanvasRenderer>();
            Instance = rect.gameObject.AddComponent<Overlay>();
            Instance.raycastTarget = true;
        }

        // ------------------------------------------------------------------ frame

        private int begunFrame = -1;

        /// <summary>Starts this frame's drawing (several tools may draw; the first call clears).</summary>
        public static void Begin() {
            if (Instance == null || Instance.begunFrame == Time.frameCount)
                return;
            Instance.begunFrame = Time.frameCount;
            Instance.lastSegments.Clear();
            Instance.lastSegments.AddRange(Instance.hitSegments);
            Instance.lastDiscs.Clear();
            Instance.lastDiscs.AddRange(Instance.hitDiscs);
            Instance.quads.Clear();
            Instance.hitSegments.Clear();
            Instance.hitDiscs.Clear();
            Instance.labelsUsed = 0;
        }

        private void LateUpdate() {
            Dimension.Tidy();
            if (begunFrame != Time.frameCount) {
                quads.Clear();
                hitSegments.Clear();
                hitDiscs.Clear();
                lastSegments.Clear();
                lastDiscs.Clear();
                labelsUsed = 0;
            }
            for (var i = 0; i < labels.Count; i++)
                labels[i].gameObject.SetActive(i < labelsUsed);
            SetVerticesDirty();
        }

        // ------------------------------------------------------------------ projection

        /// <summary>A world point in overlay units (from the bottom-left); false when behind the camera.</summary>
        public static bool ToScreen(Vector3 world, out Vector2 point) {
            var screen = ViewCamera.WorldToScreenPoint(world);
            point = Layout.FromScreen(screen);
            return screen.z > 0.01f;
        }

        public static Vector2 Mouse => Layout.FromScreen(MouseInput.Position);

        // ------------------------------------------------------------------ shapes (overlay units)

        public static void Line(Vector2 a, Vector2 b, Color color, float width = 1.5f, bool handle = false, float hitRadius = 8f) {
            if (Instance == null) return;
            var dir = b - a;
            if (dir.sqrMagnitude < 1e-6f) return;
            var n = new Vector2(-dir.y, dir.x).normalized * (width / 2f);
            Instance.quads.Add(new Quad { a = a - n, b = a + n, c = b + n, d = b - n, color = color });
            if (handle) Instance.hitSegments.Add((a, b, hitRadius));
        }

        public static void Line(Vector3 worldA, Vector3 worldB, Color color, float width = 1.5f) {
            if (ToScreen(worldA, out var a) & ToScreen(worldB, out var b))
                Line(a, b, color, width);
        }

        public static void Disc(Vector2 center, float radius, Color color, bool handle = false, int segments = 20) {
            if (Instance == null) return;
            for (var i = 0; i < segments; i++) {
                var a0 = i * Mathf.PI * 2f / segments;
                var a1 = (i + 1) * Mathf.PI * 2f / segments;
                var p0 = center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius;
                var p1 = center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius;
                Instance.quads.Add(new Quad { a = center, b = p0, c = p1, d = center, color = color });
            }
            if (handle) Instance.hitDiscs.Add((center, radius + 4f));
        }

        /// <summary>A filled square (corner markers).</summary>
        public static void Square(Vector2 center, float half, Color color) {
            if (Instance == null) return;
            Instance.quads.Add(new Quad {
                a = center + new Vector2(-half, -half), b = center + new Vector2(-half, half),
                c = center + new Vector2(half, half), d = center + new Vector2(half, -half), color = color
            });
        }

        /// <summary>A filled triangle pointing up (midpoint markers).</summary>
        public static void Triangle(Vector2 center, float size, Color color) {
            if (Instance == null) return;
            var top = center + new Vector2(0f, size);
            Instance.quads.Add(new Quad {
                a = center + new Vector2(-size * 0.9f, -size * 0.6f), b = top, c = top,
                d = center + new Vector2(size * 0.9f, -size * 0.6f), color = color
            });
        }

        /// <summary>A filled polygon (convex, or a fan from its first point), e.g. sketch regions.</summary>
        public static void Fan(IList<Vector2> points, Color color) {
            if (Instance == null || points.Count < 3) return;
            for (var i = 1; i + 1 < points.Count; i++)
                Instance.quads.Add(new Quad { a = points[0], b = points[i], c = points[i + 1], d = points[i + 1], color = color });
        }

        public static void Ring(Vector2 center, float radius, Color color, float width = 1.5f, int segments = 24) {
            for (var i = 0; i < segments; i++) {
                var a0 = i * Mathf.PI * 2f / segments;
                var a1 = (i + 1) * Mathf.PI * 2f / segments;
                Line(center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius,
                     center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius, color, width);
            }
        }

        /// <summary>A filled triangle pointing from <paramref name="from"/> towards <paramref name="tip"/>.</summary>
        public static void Arrowhead(Vector2 tip, Vector2 from, Color color, float length = 12f, float width = 9f) {
            if (Instance == null) return;
            var dir = (tip - from).normalized;
            var n = new Vector2(-dir.y, dir.x) * (width / 2f);
            var back = tip - dir * length;
            Instance.quads.Add(new Quad { a = back - n, b = tip, c = tip, d = back + n, color = color });
        }

        /// <summary>A world-space circle (around <paramref name="axis"/>) drawn as a screen polyline; returns the
        /// screen points so callers can hit-test it.</summary>
        public static List<Vector2> WorldCircle(Vector3 center, Vector3 axis, float radius, Color color, float width = 2f, bool handle = false) {
            var points = new List<Vector2>();
            axis = axis.normalized;
            var u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var v = Vector3.Cross(axis, u);
            const int segments = 64;
            for (var i = 0; i <= segments; i++) {
                var angle = i * Mathf.PI * 2f / segments;
                if (ToScreen(center + (u * Mathf.Cos(angle) + v * Mathf.Sin(angle)) * radius, out var p))
                    points.Add(p);
            }
            for (var i = 0; i + 1 < points.Count; i++)
                Line(points[i], points[i + 1], color, width, handle, 7f);
            return points;
        }

        /// <summary>Distance (overlay units) from a point to a polyline.</summary>
        public static float DistanceTo(List<Vector2> polyline, Vector2 point) {
            var best = float.MaxValue;
            for (var i = 0; i + 1 < polyline.Count; i++)
                best = Mathf.Min(best, DistanceToSegment(point, polyline[i], polyline[i + 1]));
            return best;
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b) {
            var ab = b - a;
            var t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        // ------------------------------------------------------------------ labels

        public class Label : MonoBehaviour {
            public Image background;
            public TextMeshProUGUI text;
            public Button button;
            public Action onClick;
        }

        /// <summary>A value label (pill) centred at <paramref name="at"/>; clickable when onClick is set.</summary>
        public static Label Text(Vector2 at, string value, Color color, Action onClick = null, bool emphasize = false) {
            if (Instance == null) return null;
            var label = Instance.NextLabel();
            label.text.text = value;
            label.text.color = emphasize ? Color.black : color;
            label.background.color = emphasize ? color : new Color(0.08f, 0.09f, 0.1f, 0.88f);
            var size = label.text.GetPreferredValues(value);
            var rect = (RectTransform)label.transform;
            rect.anchoredPosition = at;
            // V2: as tall as its lines (a second line was cut off with "...").
            rect.sizeDelta = new Vector2(size.x + 16f, Mathf.Max(24f, size.y + 6f));
            label.onClick = onClick;
            label.background.raycastTarget = onClick != null;
            return label;
        }

        /// <summary>V2: a tool's hint by the mouse: below and to the right of the pointer (never under it), kept on the
        /// screen.</summary>
        public static Label MouseText(string value, Color color, Action onClick = null, bool emphasize = false) {
            if (Instance == null) return null;
            var label = Text(Mouse, value, color, onClick, emphasize);
            var rect = (RectTransform)label.transform;
            var size = rect.sizeDelta;
            var screen = Instance.rectTransform.rect.size;
            var at = Mouse + new Vector2(20f + size.x / 2f, -16f - size.y / 2f);
            at.x = Mathf.Min(at.x, screen.x - size.x / 2f - 4f);
            if (at.y - size.y / 2f < 4f) at.y = Mouse.y + 24f + size.y / 2f;
            rect.anchoredPosition = at;
            return label;
        }

        private Label NextLabel() {
            if (labelsUsed < labels.Count)
                return labels[labelsUsed++];
            var background = Ui.Image(transform, "Label", Color.black, 12f, false);
            background.rectTransform.anchorMin = background.rectTransform.anchorMax = Vector2.zero;
            background.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            var label = background.gameObject.AddComponent<Label>();
            label.background = background;
            label.text = Ui.Text(background.transform, "Text", "", Theme.FontBody, Theme.Text, Theme.Medium, TextAlignmentOptions.Center);
            label.text.rectTransform.Fill();
            label.button = background.gameObject.AddComponent<Button>();
            label.button.transition = Selectable.Transition.None;
            label.button.onClick.AddListener(() => label.onClick?.Invoke());
            labels.Add(label);
            labelsUsed++;
            return label;
        }

        // ------------------------------------------------------------------ mesh and raycasts

        protected override void OnPopulateMesh(VertexHelper vh) {
            vh.Clear();
            // The rect is the whole canvas, pivot in the middle: shift from bottom-left coordinates.
            var offset = -rectTransform.rect.size * rectTransform.pivot;
            foreach (var q in quads) {
                var i = vh.currentVertCount;
                vh.AddVert(q.a + offset, q.color, Vector4.zero);
                vh.AddVert(q.b + offset, q.color, Vector4.zero);
                vh.AddVert(q.c + offset, q.color, Vector4.zero);
                vh.AddVert(q.d + offset, q.color, Vector4.zero);
                vh.AddTriangle(i, i + 1, i + 2);
                vh.AddTriangle(i, i + 2, i + 3);
            }
        }

        /// <summary>Whether the mouse is on a handle (then the interface takes the click).</summary>
        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera) {
            var p = Layout.FromScreen(screenPoint);
            foreach (var (a, b, radius) in hitSegments)
                if (DistanceToSegment(p, a, b) <= radius) return true;
            foreach (var (center, radius) in hitDiscs)
                if (Vector2.Distance(p, center) <= radius) return true;
            foreach (var (a, b, radius) in lastSegments)
                if (DistanceToSegment(p, a, b) <= radius) return true;
            foreach (var (center, radius) in lastDiscs)
                if (Vector2.Distance(p, center) <= radius) return true;
            return false;
        }
    }
}
