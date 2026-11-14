using Protobot.Interface;
using UnityEngine;

namespace Protobot.Motion {
    // V2: a cylinder's rod and air (right-click a cylinder > Rod position and air…): the rod anywhere in its stroke,
    // from all the way in to all the way out and never past either end, with what's on the rod moving along (one
    // undo step when the window closes; the arrow at the rod's end of a selected cylinder does the same by dragging),
    // and which ends get air in the simulation (one key: both ends = out, then in; one end = that way, then loose).
    public static class CylinderDialog {
        public static void Open(PneumaticCylinder cylinder) {
            if (cylinder == null || !cylinder.BeginManual()) return;
            var dialog = Dialog.Open(cylinder.Label, new Vector2(480f, 400f));
            Ui.Column(dialog.Body, 8f);
            Dialog.Heading(dialog.Body, "Rod out (" + Inch.Format(0f) + " to " + Inch.Format(cylinder.Stroke) + ")");
            // Snaps like the Transform tool (its Snap switch and Move step).
            var step = RodSlider.Step > 0f ? RodSlider.Step / Mathf.Max(cylinder.Stroke, 0.01f) : 0f;
            MotionUi.Slider(dialog.Body, cylinder.Position, 0f, step,
                f => Inch.Format(f * cylinder.Stroke) + " out", f => cylinder.ManualTo(f));
            Dialog.Heading(dialog.Body, "Air (click a fitting to connect or disconnect its air)");
            var note = default(TMPro.TextMeshProUGUI);
            var picker = AirPicker.Create(dialog.Body, cylinder, () => note.text = AirPicker.Summary(cylinder.Ports));
            picker.GetComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 0f;
            note = Ui.Text(dialog.Body, "Note", AirPicker.Summary(cylinder.Ports), Theme.FontSmall, Theme.TextDim);
            note.Size(-1, 40f);
            note.textWrappingMode = TMPro.TextWrappingModes.Normal;
            note.overflowMode = TMPro.TextOverflowModes.Overflow;
            dialog.OnClose += cylinder.EndManual;
            var done = Ui.Button(dialog.Footer, "Done", "Done", dialog.Close, true, 6f);
            done.Size(110f, 34f);
        }
    }
}
