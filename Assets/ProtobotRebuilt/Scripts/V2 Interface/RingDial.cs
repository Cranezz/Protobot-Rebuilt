using System.Collections.Generic;
using UnityEngine;

namespace Protobot.Interface {
    // V2: what a rotate ring shows while it's dragged: a dial lying in the plane the part turns in (round the turn's
    // axis, so it tilts with the ring), with a tick for every snap step (longer ones every 45°), the turn so far
    // filled in from where the drag began, and the angle beside the mouse.
    public static class RingDial {
        private static int lastFrame = -10;

        /// <param name="center">The point the part turns about (world).</param>
        /// <param name="axis">The turn's axis: a positive angle turns right-handed about it.</param>
        /// <param name="start">Where the drag began (screen pixels): 0° and the dial's size.</param>
        /// <param name="angle">The turn so far in degrees about the axis.</param>
        /// <param name="step">Degrees per tick (the snap step).</param>
        public static void Draw(Vector3 center, Vector3 axis, Vector2 start, Vector2 mouse, float angle, float step) {
            var camera = Overlay.ViewCamera;
            if (camera == null || axis.sqrMagnitude < 1e-8f) return;
            Overlay.Begin();
            lastFrame = Time.frameCount;
            axis.Normalize();

            // World size of a screen pixel at the dial.
            float perPixel;
            if (camera.orthographic) perPixel = camera.orthographicSize * 2f / Mathf.Max(1, camera.pixelHeight);
            else {
                var depth = Mathf.Max(0.01f, Vector3.Dot(center - camera.transform.position, camera.transform.forward));
                perPixel = 2f * depth * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, camera.pixelHeight);
            }

            // 0° points where the drag began: the mouse's ray onto the dial's plane (or the nearest direction in it
            // when the plane is seen edge-on).
            var ray = camera.ScreenPointToRay(start);
            var plane = new Plane(axis, center);
            Vector3 zero;
            float radius;
            if (plane.Raycast(ray, out var distance) && Mathf.Abs(Vector3.Dot(ray.direction, axis)) > 0.15f) {
                var offset = ray.GetPoint(distance) - center;
                zero = offset.sqrMagnitude > 1e-8f ? offset.normalized : Vector3.Cross(axis, camera.transform.up).normalized;
                radius = Mathf.Clamp(offset.magnitude, 70f * perPixel, 240f * perPixel);
            }
            else {
                zero = Vector3.ProjectOnPlane(camera.transform.up, axis);
                if (zero.sqrMagnitude < 1e-6f) zero = Vector3.ProjectOnPlane(camera.transform.right, axis);
                zero.Normalize();
                radius = 100f * perPixel;
            }
            Vector3 At(float degrees, float r) => center + Quaternion.AngleAxis(degrees, axis) * zero * r;

            // Ticks every step, from the start direction (0° is where the drag began).
            if (step < 1f) step = 5f;
            var ticks = Mathf.Min(360, Mathf.RoundToInt(360f / step));
            var faint = new Color(1f, 1f, 1f, 0.4f);
            var px = perPixel;
            for (var k = 0; k < ticks; k++) {
                var degrees = k * 360f / ticks;
                var major = Mathf.Abs(Mathf.Repeat(degrees + 0.5f, 45f) - 0.5f) < 0.6f;
                Overlay.Line(At(degrees, radius - (major ? 14f : 7f) * px), At(degrees, radius + (major ? 6f : 2f) * px),
                    major ? new Color(1f, 1f, 1f, 0.9f) : faint, major ? 2f : 1.2f);
            }
            // The rim, faint, so the dial reads as a disc lying in its plane.
            for (var i = 0; i < 72; i++)
                Overlay.Line(At(i * 5f, radius), At((i + 1) * 5f, radius), new Color(1f, 1f, 1f, 0.18f), 1f);

            // The turn so far: a filled wedge from the start to now.
            var swept = Mathf.Abs(angle);
            if (swept > 0.1f) {
                var parts = Mathf.Max(2, Mathf.CeilToInt(swept / 4f));
                var inner = radius - 16f * px;
                if (Overlay.ToScreen(center, out var c)) {
                    var points = new List<Vector2> { c };
                    var ok = true;
                    for (var i = 0; i <= parts && ok; i++) {
                        ok = Overlay.ToScreen(At(angle * i / parts, inner), out var p);
                        points.Add(p);
                    }
                    if (ok) Overlay.Fan(points, new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.22f));
                }
                for (var i = 0; i < parts; i++)
                    Overlay.Line(At(angle * i / parts, inner), At(angle * (i + 1) / parts, inner), Theme.Accent, 2.5f);
            }
            Overlay.Line(center, At(0f, radius + 6f * px), new Color(1f, 1f, 1f, 0.55f), 1.5f);
            Overlay.Line(center, At(angle, radius + 10f * px), Theme.Accent, 2.5f);
            if (Overlay.ToScreen(center, out var middle)) Overlay.Disc(middle, 3.5f, Color.white);

            // The angle, beside the mouse.
            var shown = Mathf.Round(angle * 10f) / 10f;
            var text = (shown > 0f ? "+" : "") + shown.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "°";
            var m = Layout.FromScreen(mouse);
            var dir = (m - middle).sqrMagnitude > 1f ? (m - middle).normalized : Vector2.right;
            Overlay.Text(m + dir * 34f, text, Theme.Accent, null, true);
        }

        /// <summary>Clears the dial once the drag is over.</summary>
        public static void Clear() {
            if (Time.frameCount - lastFrame > 1) return;
            lastFrame = -10;
            Overlay.Begin();
        }
    }
}
