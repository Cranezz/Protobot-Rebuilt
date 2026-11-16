using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.RobotCode {
    /// <summary>
    /// V2 (Phase 9): what the tuning graph shows. Two sources, both needing no change to the program (so the same code
    /// runs on the real robot):
    ///  - printed values: every "name: number" or "name = number" in a printed line is a point of the series "name"
    ///    (teams print their PID's error and output while tuning: print("error:", error, "output:", out));
    ///  - the motors the program drives: their speed (rpm) against the speed asked for, or their position (degrees)
    ///    against the position asked for, sampled 50 times a simulated second.
    /// Series keep the last 60 s; a new run clears them.
    /// </summary>
    public static class TuningData {
        public sealed class Series {
            public string name, unit;
            public Color color;
            public bool visible = true, motor;
            public readonly float[] t = new float[Capacity], v = new float[Capacity];
            public int count, head;   // ring buffer: oldest at (head - count)
            public float Last => count > 0 ? v[(head - 1 + Capacity) % Capacity] : 0f;

            public void Add(float time, float value) {
                if (float.IsNaN(value) || float.IsInfinity(value)) return;
                t[head] = time;
                v[head] = value;
                head = (head + 1) % Capacity;
                if (count < Capacity) count++;
            }

            public (float t, float v) At(int i) {   // 0 = oldest
                var k = (head - count + i + Capacity) % Capacity;
                return (t[k], v[k]);
            }
        }

        public const int Capacity = 3000;   // 60 s at 50 a second
        public static readonly List<Series> All = new();
        public static int Version;
        private static readonly Dictionary<string, bool> hidden = new();   // what was switched off stays off between runs
        private static readonly HashSet<string> chosen = new();             // series the user showed or hid by hand

        /// <summary>Shows or hides a series (the choice is kept for the next runs).</summary>
        public static void Toggle(Series s) {
            s.visible = !s.visible;
            chosen.Add(s.name);
            Version++;
        }
        private static float nextSample;

        private static readonly Color[] Palette = {
            new(0.35f, 0.75f, 1f), new(1f, 0.6f, 0.2f), new(0.45f, 0.9f, 0.45f), new(1f, 0.4f, 0.55f),
            new(0.8f, 0.6f, 1f), new(1f, 0.9f, 0.35f), new(0.4f, 0.95f, 0.9f), new(0.95f, 0.95f, 0.95f),
        };

        private static readonly Regex Pair = new(@"([A-Za-z_][A-Za-z0-9_ ]{0,24}?)\s*[:=]\s*(-?\d+(?:\.\d+)?(?:[eE]-?\d+)?)", RegexOptions.Compiled);

        internal static void Begin() {
            foreach (var s in All) hidden[s.name] = !s.visible;
            All.Clear();
            nextSample = 0f;
            Version++;
        }

        public static Series Get(string name, string unit, bool motor) {
            foreach (var s in All) if (s.name == name) return s;
            var made = new Series { name = name, unit = unit, motor = motor, color = Palette[All.Count % Palette.Length] };
            if (chosen.Contains(name) && hidden.TryGetValue(name, out var off)) made.visible = !off;
            // The program prints its own values: those are what's being tuned, so the motors' lines start hidden.
            else if (motor && All.Exists(x => !x.motor)) made.visible = false;
            else if (!motor && !All.Exists(x => !x.motor))
                foreach (var x in All)
                    if (x.motor && !chosen.Contains(x.name)) x.visible = false;
            All.Add(made);
            return made;
        }

        /// <summary>A printed line: its "name: number" pairs become points.</summary>
        internal static void Printed(float time, string text) {
            if (string.IsNullOrEmpty(text) || text.IndexOfAny(new[] { ':', '=' }) < 0) return;
            foreach (Match m in Pair.Matches(text)) {
                var name = m.Groups[1].Value.Trim();
                if (name.Length == 0 || !float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) continue;
                Get(name, "", false).Add(time, value);
                Version++;
            }
        }

        /// <summary>The motors the program drives, 50 times a simulated second.</summary>
        internal static void Sample(float time, IEnumerable<MotorControl> motors) {
            if (time < nextSample) return;
            nextSample = time + 0.02f;
            foreach (var c in motors) {
                if (c.motor == null || !c.commanded) continue;
                var name = c.motor.Label;
                switch (c.mode) {
                    case MotorControl.Mode.Position:
                        Get(name + " position", "deg", true).Add(time, (float)c.degrees);
                        Get(name + " target position", "deg", true).Add(time, (float)c.targetDeg);
                        break;
                    default:
                        Get(name + " speed", "rpm", true).Add(time, c.Level * c.FreeRpm);
                        if (c.mode == MotorControl.Mode.Velocity) Get(name + " target speed", "rpm", true).Add(time, c.target * c.FreeRpm);
                        break;
                }
            }
            Version++;
        }
    }

    /// <summary>V2 (Phase 9): the tuning graph (a tab in the code window): a line per series over the last seconds,
    /// a chip per series above it (click: show/hide; its latest value), auto-scaled, time along the bottom. It redraws
    /// at most 30 times a second and only while shown.</summary>
    public sealed class TuningGraphView : MonoBehaviour {
        private RectTransform legend, plot;
        private ShapeGraphic lines;
        private TextMeshProUGUI top, bottom, left, right, empty;
        private readonly List<(Button button, TextMeshProUGUI label, Image dot)> chips = new();
        private int shownVersion = -1, chipsFor = -1;
        private float nextDraw;
        private float window = 10f;
        private const float LegendWidth = 150f;
        private readonly List<Vector2> points = new();

        public static TuningGraphView Create(RectTransform parent) {
            var root = Ui.Rect("Tuning Graph", parent).Fill();
            var view = root.gameObject.AddComponent<TuningGraphView>();
            view.Build(root);
            return view;
        }

        private void Build(RectTransform root) {
            var bar = Ui.Rect("Bar", root);
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.offsetMin = new Vector2(8f, -30f);
            bar.offsetMax = new Vector2(-8f, -4f);
            Ui.Row(bar, 4f);
            var hint = Ui.Text(bar, "Hint", "Click a name to show or hide it", Theme.FontSmall - 1f, Theme.TextFaint);
            hint.Size(-1, 22f, 1f);
            foreach (var seconds in new[] { 5f, 10f, 30f, 60f }) {
                var s = seconds;
                var b = Ui.Button(bar, s + "s", s.ToString("0") + " s", () => { window = s; shownVersion = -1; }, false, 4f, Theme.FontSmall - 1f);
                b.Size(-1, 22f);
                Ui.Tip(b, "Show the last " + s.ToString("0") + " seconds");
            }

            // The legend: a row per series down the right (scrolls when there are many).
            var legendBox = Ui.Rect("Legend Box", root);
            legendBox.anchorMin = new Vector2(1f, 0f);
            legendBox.anchorMax = new Vector2(1f, 1f);
            legendBox.pivot = new Vector2(1f, 0.5f);
            legendBox.offsetMin = new Vector2(-LegendWidth - 8f, 8f);
            legendBox.offsetMax = new Vector2(-8f, -36f);
            legendBox.gameObject.AddComponent<RectMask2D>();
            legend = Ui.Rect("Legend", legendBox);
            legend.anchorMin = new Vector2(0f, 1f);
            legend.anchorMax = new Vector2(1f, 1f);
            legend.pivot = new Vector2(0.5f, 1f);
            legend.sizeDelta = Vector2.zero;
            Ui.Column(legend, 2f);
            legend.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = legendBox.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = legendBox;
            scroll.content = legend;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 20f;

            var box = Ui.Image(root, "Plot", new Color(0f, 0f, 0f, 0.25f), 4f).rectTransform;
            box.Fill(52, 36, LegendWidth + 16f, 22);
            plot = box;
            lines = ShapeGraphic.Create(box);
            top = Label(root, TextAlignmentOptions.TopRight, 48f);
            bottom = Label(root, TextAlignmentOptions.TopRight, 48f);
            left = Label(root, TextAlignmentOptions.TopLeft, 80f);
            right = Label(root, TextAlignmentOptions.TopRight, 80f);
            empty = Ui.Text(box, "Empty", "Run a program: the motors it drives are graphed here (speed against the speed asked for, position against the target), "
                                         + "and anything it prints as name: number, like print(\"error:\", error, \"output:\", output).",
                            Theme.FontSmall, Theme.TextFaint, null, TextAlignmentOptions.Center);
            empty.rectTransform.Fill(20, 10, 20, 10);
            empty.textWrappingMode = TextWrappingModes.Normal;
        }

        private TextMeshProUGUI Label(RectTransform root, TextAlignmentOptions align, float width) {
            var t = Ui.Text(root, "Axis", "", Theme.FontSmall - 2f, Theme.TextFaint, MonoFont.Get, align);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0f, 1f);
            t.rectTransform.pivot = new Vector2(0f, 1f);
            t.rectTransform.sizeDelta = new Vector2(width, 16f);
            t.raycastTarget = false;
            return t;
        }

        private void LateUpdate() {
            if (Time.unscaledTime < nextDraw || TuningData.Version == shownVersion) return;
            nextDraw = Time.unscaledTime + 1f / 30f;
            shownVersion = TuningData.Version;
            if (chipsFor != TuningData.All.Count) Chips();
            Draw();
        }

        private void Chips() {
            chipsFor = TuningData.All.Count;
            foreach (var c in chips) Destroy(c.button.gameObject);
            chips.Clear();
            foreach (var s in TuningData.All) {
                var series = s;
                var row = Ui.Image(legend, s.name, new Color(1f, 1f, 1f, 0f), 3f, true);
                var button = row.gameObject.AddComponent<Button>();
                button.targetGraphic = row;
                var colors = button.colors;
                colors.highlightedColor = new Color(1f, 1f, 1f, 0.08f);
                colors.normalColor = Color.white;
                button.colors = colors;
                button.onClick.AddListener(() => {
                    TuningData.Toggle(series);
                    shownVersion = -1;
                });
                row.rectTransform.Size(-1, 18f);
                Ui.Row(row.rectTransform, 5f, new RectOffset(4, 2, 0, 0));
                var dot = Ui.Image(row.transform, "Dot", s.color, 3f);
                dot.rectTransform.Size(10f, 4f);
                dot.raycastTarget = false;
                var label = Ui.Text(row.transform, "Label", s.name, Theme.FontSmall - 2f, Theme.Text);
                label.Size(-1, 18f, 1f);
                label.raycastTarget = false;
                label.overflowMode = TextOverflowModes.Ellipsis;
                Ui.Tip(row, s.name, null, "Click to show or hide it." + (s.motor ? " A motor the program drives." : " Printed by the program (name: number)."));
                chips.Add((button, label, dot));
            }
        }

        private void Draw() {
            lines.Clear();
            var r = plot.rect;
            var visible = TuningData.All.FindAll(s => s.visible && s.count > 0);
            empty.gameObject.SetActive(TuningData.All.Count == 0);
            for (var i = 0; i < chips.Count && i < TuningData.All.Count; i++) {
                var s = TuningData.All[i];
                chips[i].label.text = s.name + "  <color=#9AA3AD>" + s.Last.ToString(Mathf.Abs(s.Last) >= 100f ? "0" : "0.##", CultureInfo.InvariantCulture) + (s.unit.Length > 0 ? " " + s.unit : "") + "</color>";
                chips[i].label.color = s.visible ? Theme.Text : Theme.TextFaint;
                chips[i].dot.color = s.visible ? s.color : new Color(s.color.r, s.color.g, s.color.b, 0.25f);
            }
            if (visible.Count == 0 || r.width < 10f) {
                top.text = bottom.text = left.text = right.text = "";
                return;
            }
            var end = 0f;
            foreach (var s in visible) end = Mathf.Max(end, s.At(s.count - 1).t);
            var start = end - window;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var s in visible)
                for (var i = 0; i < s.count; i++) {
                    var (t, v) = s.At(i);
                    if (t < start) continue;
                    lo = Mathf.Min(lo, v);
                    hi = Mathf.Max(hi, v);
                }
            if (lo > hi) { lo = 0f; hi = 1f; }
            if (hi - lo < 1e-3f) { hi += 1f; lo -= 1f; }
            var pad = (hi - lo) * 0.08f;
            lo -= pad;
            hi += pad;

            // Zero line and quarter lines.
            for (var k = 1; k < 4; k++) {
                var y = r.yMin + r.height * k / 4f;
                lines.Line(new Vector2(r.xMin, y), new Vector2(r.xMax, y), new Color(1f, 1f, 1f, 0.06f), 1f);
            }
            if (lo < 0f && hi > 0f) {
                var y0 = r.yMin + (0f - lo) / (hi - lo) * r.height;
                lines.Line(new Vector2(r.xMin, y0), new Vector2(r.xMax, y0), new Color(1f, 1f, 1f, 0.2f), 1f);
            }
            foreach (var s in visible) {
                points.Clear();
                // At most a point per pixel column (60 s at 50 a second would be 3000).
                var step = Mathf.Max(1, s.count / Mathf.Max(1, Mathf.RoundToInt(r.width)));
                for (var i = 0; i < s.count; i += step) {
                    var (t, v) = s.At(i);
                    if (t < start) continue;
                    points.Add(new Vector2(r.xMin + (t - start) / window * r.width, r.yMin + (v - lo) / (hi - lo) * r.height));
                }
                var (lt, lv) = s.At(s.count - 1);
                points.Add(new Vector2(r.xMin + (lt - start) / window * r.width, r.yMin + (lv - lo) / (hi - lo) * r.height));
                if (points.Count >= 2) lines.Polyline(points, s.color, s.name.Contains("target") ? 1f : 1.6f);
            }
            var box = plot.offsetMin;
            Place(top, new Vector2(box.x - 52f, -36f), Format(hi));
            Place(bottom, new Vector2(box.x - 52f, -(((RectTransform)transform).rect.height - 22f - 14f)), Format(lo));
            Place(left, new Vector2(box.x, -(((RectTransform)transform).rect.height - 20f)), Mathf.Max(0f, start).ToString("0.0", CultureInfo.InvariantCulture) + " s");
            Place(right, new Vector2(((RectTransform)transform).rect.width - LegendWidth - 16f - 80f, -(((RectTransform)transform).rect.height - 20f)), end.ToString("0.0", CultureInfo.InvariantCulture) + " s");
        }

        private static void Place(TextMeshProUGUI t, Vector2 at, string text) {
            t.rectTransform.anchoredPosition = at;
            t.text = text;
        }

        private static string Format(float v) => Mathf.Abs(v) >= 100f ? v.ToString("0", CultureInfo.InvariantCulture) : v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
