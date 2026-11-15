using System.Collections.Generic;
using Protobot.Interface;
using UnityEngine;

namespace Protobot.Devices {
    // V2: small line icons for the devices (the Devices panel's port grid and cards), drawn in a rect: a motor with
    // its cartridge band, a cylinder with its rod, a distance sensor's beam, the optical sensor's eye...
    public static class DeviceIcons {
        private static readonly Color Line = new(0.82f, 0.85f, 0.9f, 1f);

        /// <summary>Draws a device's icon filling a rect (the graphic's own units), its accent in `tint`.</summary>
        public static void Draw(ShapeGraphic g, Rect r, DeviceKind kind, Color tint) {
            var s = Mathf.Min(r.width, r.height);
            var c = r.center;
            Vector2 P(float x, float y) => c + new Vector2(x, y) * (s / 2f);   // -1..1 square
            var w = Mathf.Max(1.2f, s / 13f);
            void Box(float x0, float y0, float x1, float y1, Color color) =>
                g.Polyline(new[] { P(x0, y0), P(x0, y1), P(x1, y1), P(x1, y0) }, color, w, true);
            void Fill(float x0, float y0, float x1, float y1, Color color) => g.Quad(P(x0, y0), P(x0, y1), P(x1, y1), P(x1, y0), color);
            void Arc(Vector2 center, float radius, float from, float to, Color color, float width) {
                var points = new List<Vector2>();
                for (var i = 0; i <= 14; i++) {
                    var a = Mathf.Lerp(from, to, i / 14f) * Mathf.Deg2Rad;
                    points.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
                }
                g.Polyline(points, color, width);
            }
            switch (kind) {
                case DeviceKind.Motor:
                    // The housing, its square shaft hole, and the cartridge's colored window.
                    Box(-0.85f, -0.85f, 0.85f, 0.85f, Line);
                    g.Ring(P(0f, 0.12f), s * 0.2f, Line, w * 0.8f, 20);
                    Fill(-0.08f, 0.04f, 0.08f, 0.2f, Line);
                    Arc(P(0f, 0.12f), s * 0.33f, 215f, 325f, tint, w * 1.9f);
                    break;
                case DeviceKind.Solenoid:
                    // A cylinder: barrel, piston rod, rod end.
                    Box(-0.9f, -0.38f, 0.25f, 0.38f, Line);
                    Fill(-0.9f, -0.38f, -0.6f, 0.38f, tint);
                    g.Line(P(0.25f, 0f), P(0.85f, 0f), Line, w * 1.4f);
                    Fill(0.78f, -0.2f, 0.92f, 0.2f, Line);
                    break;
                case DeviceKind.Distance:
                case DeviceKind.Ultrasonic:
                    Box(-0.9f, -0.45f, -0.35f, 0.45f, Line);
                    if (kind == DeviceKind.Ultrasonic) {
                        Arc(P(-0.35f, 0f), s / 2f * 0.45f, -40f, 40f, Theme.Accent, w);
                        Arc(P(-0.35f, 0f), s / 2f * 0.85f, -40f, 40f, Theme.Accent, w);
                    }
                    else {
                        g.Dashed(P(-0.25f, 0f), P(0.95f, 0f), Theme.Accent, w, s * 0.12f, s * 0.07f);
                        g.Disc(P(0.75f, 0f), s * 0.09f, Theme.Accent);
                    }
                    break;
                case DeviceKind.Optical:
                case DeviceKind.Light:
                    if (kind == DeviceKind.Light) {
                        g.Disc(P(0f, 0f), s * 0.2f, Theme.Warning);
                        for (var i = 0; i < 8; i++) {
                            var a = i * 45f * Mathf.Deg2Rad;
                            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                            g.Line(c + d * s * 0.32f, c + d * s * 0.46f, Theme.Warning, w);
                        }
                    }
                    else {
                        // An eye.
                        Arc(P(0f, -0.9f), s / 2f * 1.45f, 52f, 128f, Line, w);
                        Arc(P(0f, 0.9f), s / 2f * 1.45f, 232f, 308f, Line, w);
                        g.Disc(P(0f, 0f), s * 0.17f, Theme.Accent);
                    }
                    break;
                case DeviceKind.Rotation:
                case DeviceKind.Encoder:
                case DeviceKind.Potentiometer:
                case DeviceKind.Gyro:
                    g.Ring(P(0f, 0f), s * 0.36f, Line, w, 24);
                    Fill(-0.13f, -0.13f, 0.13f, 0.13f, Line);
                    if (kind == DeviceKind.Potentiometer) g.Line(P(0f, 0f), P(-0.45f, 0.5f), Theme.Accent, w * 1.3f);
                    else {
                        Arc(P(0f, 0f), s * 0.46f, 20f, 150f, Theme.Accent, w * 1.2f);
                        var tip = P(Mathf.Cos(20f * Mathf.Deg2Rad) * 0.92f, Mathf.Sin(20f * Mathf.Deg2Rad) * 0.92f);
                        g.Triangle(tip + new Vector2(-s * 0.12f, -s * 0.02f), tip + new Vector2(s * 0.1f, -s * 0.02f), tip + new Vector2(-0.01f, -s * 0.16f), Theme.Accent);
                    }
                    break;
                case DeviceKind.Inertial:
                case DeviceKind.Accelerometer:
                    g.Line(P(-0.3f, -0.4f), P(0.75f, -0.4f), Theme.AxisX, w * 1.2f);
                    g.Line(P(-0.3f, -0.4f), P(-0.3f, 0.8f), Theme.AxisY, w * 1.2f);
                    g.Line(P(-0.3f, -0.4f), P(-0.85f, -0.85f), Theme.AxisZ, w * 1.2f);
                    g.Disc(P(-0.3f, -0.4f), s * 0.07f, Line);
                    break;
                case DeviceKind.Vision:
                    Box(-0.85f, -0.55f, 0.85f, 0.55f, Line);
                    g.Ring(P(0f, 0f), s * 0.2f, Theme.Accent, w, 18);
                    break;
                case DeviceKind.Gps:
                    g.Ring(P(0f, 0f), s * 0.34f, Line, w, 24);
                    g.Line(P(0f, -0.9f), P(0f, 0.9f), Theme.Accent, w);
                    g.Line(P(-0.9f, 0f), P(0.9f, 0f), Theme.Accent, w);
                    break;
                case DeviceKind.Radio:
                    Box(-0.55f, -0.85f, 0.55f, -0.1f, Line);
                    g.Line(P(0.3f, -0.1f), P(0.3f, 0.75f), Line, w);
                    Arc(P(0.3f, 0.6f), s * 0.18f, -40f, 40f, Theme.Accent, w);
                    Arc(P(0.3f, 0.6f), s * 0.18f, 140f, 220f, Theme.Accent, w);
                    break;
                case DeviceKind.LineTracker:
                    Box(-0.6f, -0.1f, 0.6f, 0.75f, Line);
                    g.Line(P(-0.95f, -0.75f), P(0.95f, -0.75f), Theme.Accent, w * 1.6f);
                    g.Dashed(P(0f, -0.1f), P(0f, -0.65f), Theme.Accent, w, s * 0.08f, s * 0.05f);
                    break;
                case DeviceKind.Bumper:
                    Box(-0.8f, -0.75f, 0.8f, 0.15f, Line);
                    Fill(-0.35f, 0.15f, 0.35f, 0.55f, Theme.Danger);
                    break;
                case DeviceKind.Limit:
                    Box(-0.8f, -0.75f, 0.4f, 0.0f, Line);
                    g.Line(P(-0.5f, 0.0f), P(0.85f, 0.55f), Line, w * 1.2f);
                    g.Disc(P(0.85f, 0.55f), s * 0.08f, Theme.Danger);
                    break;
                case DeviceKind.Expander:
                    g.Polyline(new[] { P(-0.75f, -0.6f), P(0f, 0.7f), P(0.75f, -0.6f) }, Theme.Warning, w, true);
                    break;
                default:
                    Box(-0.7f, -0.7f, 0.7f, 0.7f, Line);
                    break;
            }
        }
    }
}
