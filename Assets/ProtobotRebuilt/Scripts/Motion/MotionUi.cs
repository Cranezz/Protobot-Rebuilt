using System;
using System.Collections.Generic;
using System.Linq;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Motion {
    // V2: widgets for the motor and sandbox controls: a key you set by clicking it and pressing a key, a slider,
    // and the labels drawn over motors and cylinders (their names, keys and which way they turn or push).
    public static class MotionUi {
        // ------------------------------------------------------------------ setting a key

        public static bool Capturing => capture != null;
        private static Action<Key> capture;
        private static TextMeshProUGUI captureLabel;
        private static CaptureHost captureHost;

        private class CaptureHost : MonoBehaviour {
            private void Update() {
                if (capture == null) return;
                var key = Controls.PressedKey();
                if (key == Key.None) {
                    if (Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame)
                        && Time.frameCount > startFrame + 1)
                        End(null);
                    return;
                }
                InputEvents.TextInput.EditEndedFrame = Time.frameCount;  // the key isn't a shortcut too
                if (key == Key.Escape) {
                    End(null);
                    Interface.Interface.Say("Key not changed", true);
                    return;
                }
                var set = capture;
                End(null);
                set(key == Key.Backspace ? Key.None : key);
            }

            public int startFrame;
        }

        private static void End(string text) {
            capture = null;
            if (captureLabel != null && text != null) captureLabel.text = text;
            captureLabel = null;
        }

        /// <summary>A key cap button: click it, then press a key (Esc keeps it, Backspace: none).</summary>
        public static Button KeyChip(Transform parent, Key key, Action<Key> set, string title, string description, bool mixed = false) {
            var button = Ui.Button(parent, title, mixed ? "mixed" : Controls.KeyName(key), null, false, 5f, Theme.FontSmall);
            button.targetGraphic.color = Theme.Field;
            button.Size(-1, 26f);
            button.GetComponent<LayoutElement>().minWidth = 44f;
            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            button.onClick.AddListener(() => {
                if (captureHost == null) captureHost = new GameObject("Key Capture").AddComponent<CaptureHost>();
                captureHost.startFrame = Time.frameCount;
                if (captureLabel != null && captureLabel != label) captureLabel.text = "…";
                captureLabel = label;
                label.text = "press a key";
                capture = k => {
                    if (label != null) label.text = Controls.KeyName(k);
                    set(k);
                };
                EventSystem.current?.SetSelectedGameObject(null);
            });
            Ui.Tip(button, title, null, description + " Click, then press the key (Esc: keep it, Backspace: no key).");
            return button;
        }

        // ------------------------------------------------------------------ a slider

        private class SliderDrag : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IScrollHandler {
            public RectTransform track;
            public Image fill;
            public RectTransform handle;
            public float value, min, step;
            public Action<float> changed, done;
            public Func<float, string> format;
            public TextMeshProUGUI label;

            public void Show() {
                var t = Mathf.InverseLerp(0f, 1f, value);
                fill.rectTransform.anchorMax = new Vector2(t, 1f);
                handle.anchorMin = handle.anchorMax = new Vector2(t, 0.5f);
                if (label != null) label.text = format(value);
            }

            private void Set(PointerEventData e) {
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(track, e.position, e.pressEventCamera, out var local)) return;
                var t = Mathf.Clamp01((local.x - track.rect.xMin) / Mathf.Max(1f, track.rect.width));
                Apply(t);
            }

            private void Apply(float t) {
                t = Mathf.Max(min, step > 0f ? Mathf.Round(t / step) * step : t);   // V2: step 0 = smooth
                if (Mathf.Approximately(t, value)) return;
                value = t;
                Show();
                changed?.Invoke(value);
            }

            public void OnPointerDown(PointerEventData e) => Set(e);
            public void OnDrag(PointerEventData e) => Set(e);
            public void OnPointerUp(PointerEventData e) => done?.Invoke(value);

            public void OnScroll(PointerEventData e) {
                Apply(Mathf.Clamp01(value + Mathf.Sign(e.scrollDelta.y) * (step > 0f ? step : 0.05f)));
                done?.Invoke(value);
            }
        }

        /// <summary>A 0..1 slider (steps of <paramref name="step"/>, never under <paramref name="min"/>) with its value beside it.</summary>
        public static RectTransform Slider(Transform parent, float value, float min, float step, Func<float, string> format, Action<float> changed, Action<float> done = null) {
            var row = Ui.Rect("Slider", parent);
            Ui.Row(row, 10f);
            row.Size(-1, 26f);
            var track = Ui.Image(row, "Track", Theme.Field, 4f, true);
            track.Size(-1, 8f, 1f);
            var fill = Ui.Image(track.transform, "Fill", Theme.Accent, 4f);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(value, 1f);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            var handle = Ui.Image(track.transform, "Handle", Color.white, 8f);
            handle.rectTransform.sizeDelta = new Vector2(16f, 16f);
            handle.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            var label = Ui.Text(row, "Value", "", Theme.FontSmall, Theme.Text);
            label.Size(112f, 26f);
            var drag = track.gameObject.AddComponent<SliderDrag>();
            drag.track = track.rectTransform;
            drag.fill = fill;
            drag.handle = handle.rectTransform;
            drag.value = value;
            drag.min = min;
            drag.step = step;
            drag.changed = changed;
            drag.done = done;
            drag.format = format;
            drag.label = label;
            drag.Show();
            Ui.Tip(track, "Speed", null, "Drag, or scroll over it.");
            return row;
        }

        // ------------------------------------------------------------------ labels over the parts

        /// <summary>Each motor's name over it with an arrow round its shaft the way forward turns it; each
        /// cylinder's name with an arrow the way it extends.</summary>
        public static void DrawLabels(IEnumerable<MotorDrive> motors, IEnumerable<PneumaticCylinder> cylinders, bool keys = true) {
            Overlay.Begin();
            var labels = new List<(Vector2 at, string text, Color color)>();
            foreach (var motor in motors) {
                if (motor == null || motor.gameObject.IsDeleted()) continue;
                var color = motor.Color;
                // V2: in the simulation the motors are where their bodies have carried them.
                var go = motor.gameObject;
                var axis = Sandbox.SimDirection(go, motor.Forward) * (Controls.Reversed(go) ? -1f : 1f);
                var center = Sandbox.SimPoint(go, motor.ShaftPoint + motor.Forward * 0.6f);
                Arc(center, axis, 0.9f, color);
                if (!motor.TryGetComponent(out Renderer renderer) || !Overlay.ToScreen(Sandbox.SimPoint(go, renderer.bounds.center), out var point)) continue;
                var text = motor.Label;
                var side = Controls.Side(motor.gameObject);
                if (Sandbox.Running && side != "") text += side == "L" ? " · drive left" : " · drive right";
                else if (keys) text += " · " + Controls.KeyName(Controls.ForwardKey(motor.gameObject)) + " / " + Controls.KeyName(Controls.ReverseKey(motor.gameObject));
                labels.Add((point + new Vector2(0f, 30f), text, color));
            }
            foreach (var cylinder in cylinders) {
                if (cylinder == null || cylinder.gameObject.IsDeleted()) continue;
                var color = Theme.Warning;
                var tip = Sandbox.SimPoint(cylinder.gameObject, cylinder.RodTip);
                var dir = Sandbox.SimDirection(cylinder.gameObject, cylinder.ExtendDirection);
                Overlay.Line(tip, tip + dir * 1.2f, color, 2.5f);
                if (Overlay.ToScreen(tip + dir * 1.2f, out var head) && Overlay.ToScreen(tip + dir * 0.8f, out var from))
                    Overlay.Arrowhead(head, from, color);
                if (!cylinder.TryGetComponent(out Renderer renderer) || !Overlay.ToScreen(Sandbox.SimPoint(cylinder.gameObject, renderer.bounds.center), out var point)) continue;
                var text = cylinder.Label + (keys ? " · " + Controls.KeyName(Controls.CylinderKey(cylinder.gameObject)) : "");
                labels.Add((point + new Vector2(0f, 30f), text, color));
            }
            PlaceLabels(labels);
        }

        // V2: labels that would sit on each other (a 6-motor drive's motors side by side) are moved apart: each goes
        // down past the ones already placed that it would touch, with a thin line back to where it belongs.
        private static void PlaceLabels(List<(Vector2 at, string text, Color color)> labels) {
            const float Height = 26f, Gap = 4f;
            var placed = new List<Rect>();
            foreach (var (at, text, color) in labels.OrderByDescending(l => l.at.y).ThenBy(l => l.at.x)) {
                var width = 18f + text.Length * 7.2f;   // about the label's width at its size
                var rect = new Rect(at.x - width / 2f, at.y - Height / 2f, width, Height);
                for (var tries = 0; tries < 20; tries++) {
                    var hit = placed.FirstOrDefault(r => r.Overlaps(rect));
                    if (hit == default) break;
                    rect.y = hit.yMin - Height - Gap;   // (screen y goes up: below the one it touched)
                }
                placed.Add(rect);
                var where = rect.center;
                if ((where - at).sqrMagnitude > 4f) Overlay.Line(at, new Vector2(where.x, rect.yMax), new Color(color.r, color.g, color.b, 0.5f), 1f);
                Overlay.Text(where, text, color, null, true);
            }
        }

        // A turning arrow: most of a circle about the axis, with its head the way it turns (right hand).
        private static void Arc(Vector3 center, Vector3 axis, float radius, Color color) {
            var side = Vector3.Cross(axis, Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up).normalized;
            var up = Vector3.Cross(axis, side);
            Vector2 last = default, beforeLast = default;
            var have = false;
            const int Steps = 28;
            for (var i = 0; i <= Steps; i++) {
                var a = Mathf.Deg2Rad * (20f + 280f * i / Steps);
                var p = center + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * radius;
                if (!Overlay.ToScreen(p, out var s)) {
                    have = false;
                    continue;
                }
                if (have) Overlay.Line(last, s, color, 3.5f);
                beforeLast = last;
                last = s;
                have = true;
            }
            if (have && (last - beforeLast).sqrMagnitude > 0.5f) Overlay.Arrowhead(last, beforeLast, color, 16f, 13f);
        }
    }
}
