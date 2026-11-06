using System.Collections.Generic;
using System.Linq;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Motion {
    // V2: the robot's control layout on screen, like a driver's controller sheet: the drivetrain's keys for its
    // control (tank, arcade, split arcade), then every other motor's forward/reverse keys and every cylinder's key,
    // by name. Read-only; it follows the build (keys, sides and the drive control are saved with it). "Edit" opens
    // the Controls window where they're set.
    public class ControlsCard : MonoBehaviour {
        private RectTransform body;
        private string shown;
        private System.Action edit;

        /// <summary>A card anchored to the parent's top right, `top` pixels down. `edit` (optional) adds an Edit button.</summary>
        public static ControlsCard Create(RectTransform parent, float top, System.Action edit = null) {
            var panel = Ui.Panel(parent, "Controls Card", Theme.Panel, 10f).rectTransform;
            panel.anchorMin = panel.anchorMax = new Vector2(1f, 1f);
            panel.pivot = new Vector2(1f, 1f);
            panel.anchoredPosition = new Vector2(-14f, -top);
            panel.sizeDelta = new Vector2(300f, 0f);
            panel.GetChild(0).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Ui.Column(panel, 4f, new RectOffset(12, 12, 10, 12));
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var card = panel.gameObject.AddComponent<ControlsCard>();
            card.edit = edit;

            var header = Ui.Rect("Header", panel);
            Ui.Row(header, 6f);
            header.Size(-1, 26f);
            var title = Ui.Text(header, "Title", "CONTROLS", 12.5f, Theme.TextFaint, Theme.Medium);
            title.characterSpacing = 6f;
            title.Size(-1, 24f, 1f);
            if (edit != null) {
                var button = Ui.Button(header, "Edit", "Edit", () => edit(), false, 5f, Theme.FontSmall);
                button.Size(-1, 24f);
                Ui.Tip(button, "Edit controls", null, "The drive control, each motor's side (left/right drive) and keys, each cylinder's key. They're saved with the build.");
            }
            card.body = Ui.Rect("Body", panel);
            Ui.Column(card.body, 4f);
            card.body.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            card.Refresh();
            return card;
        }

        private static string Signature() => Controls.Drive + "#" + (Gamepad.current != null) + "#" +
            string.Join("|", MotorDrive.Live().Select(m => m.Label + Controls.ForwardKey(m.gameObject) + Controls.ReverseKey(m.gameObject) + Controls.Side(m.gameObject)))
            + "#" + string.Join("|", Controls.Cylinders().Select(c => c.Label + Controls.CylinderKey(c.gameObject)));

        private void Update() {
            if (Time.frameCount % 15 == 0) Refresh();
        }

        private void Refresh() {
            var signature = Signature();
            if (signature == shown) return;
            shown = signature;
            for (var i = body.childCount - 1; i >= 0; i--) Destroy(body.GetChild(i).gameObject);

            var motors = MotorDrive.Live();
            var drive = motors.Where(m => Controls.Side(m.gameObject) != "").ToList();
            Section(Controls.DriveModeNames[(int)Controls.Drive] + " drive");
            if (drive.Count > 0) {
                foreach (var (keys, action) in Controls.DriveLayout(Controls.Drive)) Line(keys.Split(new[] { " / " }, System.StringSplitOptions.None), action);
                var left = drive.Count(m => Controls.Side(m.gameObject) == "L");
                Note($"{left} left, {drive.Count - left} right motor{(drive.Count - left == 1 ? "" : "s")}" + (Gamepad.current != null ? " · gamepad sticks drive too" : ""));
            }
            else Note(motors.Count == 0 ? "No motors in the build" : "No drive motors: Edit and set motors to L or R");

            var others = motors.Where(m => Controls.Side(m.gameObject) == "").ToList();
            if (others.Count > 0) {
                Section("Motors");
                foreach (var motor in others) {
                    var f = Controls.ForwardKey(motor.gameObject);
                    var r = Controls.ReverseKey(motor.gameObject);
                    var keys = new List<string>();
                    if (f != Key.None) keys.Add(Controls.KeyName(f));
                    if (r != Key.None) keys.Add(Controls.KeyName(r));
                    Line(keys.Count > 0 ? keys.ToArray() : new[] { "-" }, motor.Label + (keys.Count == 2 ? " forward / reverse" : f != Key.None ? " forward" : r != Key.None ? " reverse" : ": no keys"));
                }
            }
            var cylinders = Controls.Cylinders();
            if (cylinders.Count > 0) {
                Section("Cylinders");
                foreach (var cylinder in cylinders) {
                    var key = Controls.CylinderKey(cylinder.gameObject);
                    Line(new[] { key == Key.None ? "-" : Controls.KeyName(key) }, cylinder.Label + (key == Key.None ? ": no key" : " extend / retract"));
                }
            }
        }

        private void Section(string text) {
            var t = Ui.Text(body, "Section", text, Theme.FontSmall, Theme.Text, Theme.Medium);
            t.Size(-1, 22f);
        }

        private void Note(string text) {
            var t = Ui.Text(body, "Note", text, Theme.FontSmall - 1f, Theme.TextFaint);
            t.textWrappingMode = TextWrappingModes.Normal;
            t.Size(-1, 18f);
        }

        // Key chips, then what they do.
        private void Line(string[] keys, string action) {
            var row = Ui.Rect("Line", body);
            Ui.Row(row, 4f);
            row.Size(-1, 24f);
            foreach (var key in keys) {
                var chip = Ui.Image(row, "Key", Theme.Field, 5f);
                chip.Size(Mathf.Max(24f, 12f + key.Length * 8f), 22f);
                var label = Ui.Text(chip.transform, "Text", key, Theme.FontSmall, Theme.Text, Theme.Medium, TextAlignmentOptions.Center);
                label.rectTransform.Fill();
            }
            var what = Ui.Text(row, "Action", action, Theme.FontSmall, Theme.TextDim);
            what.overflowMode = TextOverflowModes.Ellipsis;
            what.Size(-1, 22f, 1f);
        }
    }
}
