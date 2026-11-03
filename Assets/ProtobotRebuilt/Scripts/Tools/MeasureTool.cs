using Protobot.Interface;
using Protobot.InputEvents;
using Protobot.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Tools {
    // V2: the measure tool, like measuring in a CAD program. Click a hole (its centre or the centre of one of its
    // faces), a corner, an edge (its middle or any point along it) or a face, then another: the straight distance
    // between them is drawn, with how far apart they are along X, Y and Z (in the axis colors, and in the tool
    // options bar). Drag an end onto another point to move it; a click elsewhere starts again; Esc clears.
    public class MeasureTool : MonoBehaviour {
        [SerializeField] private FeedbackDisplay feedback;

        private FeaturePicker.Feature first, second;
        private static MeasureTool active;

        [RuntimeInitializeOnLoadMethod]
        private static void RegisterOptions() => ToolRail.RegisterOptions("Measure", BuildOptions);

        private static void BuildOptions(RectTransform bar) {
            if (active == null || !active.first.Valid) {
                ToolOptions.Note(bar, "Click a hole, corner, edge or face");
                return;
            }
            if (!active.second.Valid) {
                ToolOptions.Note(bar, "From " + active.first.Describe().ToLowerInvariant() + ": click a second point");
                Ui.Hint(bar, "Esc", "clear");
                return;
            }
            var d = active.second.point - active.first.point;
            var total = ToolOptions.Note(bar, "Distance  " + Inch.Format(d.magnitude));
            total.color = Theme.Text;
            for (var i = 0; i < 3; i++) {
                var axis = ToolOptions.Note(bar, (i == 0 ? "X " : i == 1 ? "Y " : "Z ") + Inch.Format(Mathf.Abs(d[i])));
                axis.color = Theme.Axis(i);
            }
            Ui.Hint(bar, "Drag", "an end to move it", "Esc", "clear");
        }

        private void OnEnable() {
            active = this;
            first = second = default;
            ToolOptions.Rebuild();
        }

        private void OnDisable() {
            if (active == this) active = null;
        }

        private void Update() {
            Overlay.Begin();
            if (!TextInput.IsTyping && !TextInput.ShortcutsPaused && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && first.Valid) {
                first = second = default;
                ToolOptions.Rebuild();
            }

            var hover = MouseInput.withinScreen && !MouseInput.overUI ? FeaturePicker.Pick() : default;
            FeaturePicker.DrawHover(hover, Theme.Accent);

            // Drag an end of the measurement onto another point.
            if (UpdateEndDrag(hover))
                return;

            if (MouseInput.LeftButton.wasPressedThisFrame && !MouseInput.overUI && MouseInput.withinScreen) {
                if (!hover.Valid)
                    feedback.Show("Click a hole, corner, edge or face to measure", FeedbackDisplay.DisplayType.alert);
                else if (!first.Valid || second.Valid) {
                    first = hover;
                    second = default;
                }
                else {
                    second = hover;
                    feedback.Show("Distance " + Inch.Format((second.point - first.point).magnitude), FeedbackDisplay.DisplayType.success);
                }
                ToolOptions.Rebuild();
            }

            Draw(hover);
        }

        // 0 none, 1 the first end, 2 the second.
        private int draggingEnd;

        /// <summary>Pressing on an end and dragging moves it to the point it's dropped on. True while dragging.</summary>
        private bool UpdateEndDrag(FeaturePicker.Feature hover) {
            if (draggingEnd == 0) {
                if (!MouseInput.LeftButton.wasPressedThisFrame || MouseInput.overUI || !second.Valid)
                    return false;
                var mouse = Overlay.Mouse;
                if (Overlay.ToScreen(first.point, out var a) && Vector2.Distance(a, mouse) < 9f) draggingEnd = 1;
                else if (Overlay.ToScreen(second.point, out var b) && Vector2.Distance(b, mouse) < 9f) draggingEnd = 2;
                // A press right on an end is a drag, not a new measurement.
                if (draggingEnd == 0) return false;
            }
            if (MouseInput.LeftButton.isPressed) {
                var moving = draggingEnd == 1 ? first : second;
                if (hover.Valid) moving = hover;
                if (draggingEnd == 1) DrawLeg(moving, second); else DrawLeg(first, moving);
                return true;
            }
            if (hover.Valid) {
                if (draggingEnd == 1) first = hover; else second = hover;
                feedback.Show("Distance " + Inch.Format((second.point - first.point).magnitude), FeedbackDisplay.DisplayType.success);
            }
            draggingEnd = 0;
            ToolOptions.Rebuild();
            return false;
        }

        private void DrawLeg(FeaturePicker.Feature a, FeaturePicker.Feature b) {
            var keepFirst = first;
            var keepSecond = second;
            first = a;
            second = b;
            Draw(default);
            first = keepFirst;
            second = keepSecond;
        }

        private void Draw(FeaturePicker.Feature hover) {
            if (!first.Valid) return;
            FeaturePicker.Draw(first, Theme.Dimension);
            var end = second.Valid ? second : hover;
            if (!end.Valid) return;
            if (second.Valid) FeaturePicker.Draw(second, Theme.Dimension);

            var a = first.point;
            var b = end.point;
            // The X, Y and Z legs, faint, then the straight line with its length.
            var x = new Vector3(b.x, a.y, a.z);
            var y = new Vector3(b.x, b.y, a.z);
            var faint = 0.55f;
            Overlay.Line(a, x, WithAlpha(Theme.AxisX, faint), 1.2f);
            Overlay.Line(x, y, WithAlpha(Theme.AxisY, faint), 1.2f);
            Overlay.Line(y, b, WithAlpha(Theme.AxisZ, faint), 1.2f);
            if (Overlay.ToScreen(a, out var sa) && Overlay.ToScreen(b, out var sb)) {
                Overlay.Line(sa, sb, Theme.Dimension, 2f);
                if (Vector2.Distance(sa, sb) > 30f) {
                    Overlay.Arrowhead(sa, sb, Theme.Dimension);
                    Overlay.Arrowhead(sb, sa, Theme.Dimension);
                }
                Overlay.Text((sa + sb) / 2f + new Vector2(0f, 16f), Inch.Format((b - a).magnitude), Theme.Dimension, null, true);
            }
        }

        private static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);
    }
}
