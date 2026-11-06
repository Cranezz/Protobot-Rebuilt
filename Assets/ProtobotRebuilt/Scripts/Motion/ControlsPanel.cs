using System.Linq;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Motion {
    // V2: every motor and cylinder in the build and what drives it, like a PROS program's controller setup: the
    // drivetrain's control (tank, arcade, split arcade; W/A/S/D and the arrows, or a gamepad's sticks) and which
    // motors are its left and right side; every other motor's forward and reverse keys (give several motors one key
    // and they run together), reversed; each cylinder's key (it extends and retracts in turn). In the sandbox it's
    // the simulation's window (pause, reset, stop); in the build it opens from Edit > Controls. While the mouse is on
    // it, every motor and cylinder shows its name, key and which way it turns or pushes.
    public class ControlsPanel : MonoBehaviour {
        private static ControlsPanel open;

        private RectTransform window, body;
        private TextMeshProUGUI title, status;
        private string shown;
        private bool simulation, right;

        /// <summary>Opens it; `right`: at the top right (beside the game's controls card) instead of by the tool rail.</summary>
        public static void Open() => Open(false);

        public static void Open(bool right) {
            if (open != null) {
                if (open.simulation != Sandbox.Running) {
                    Destroy(open.gameObject);
                    open = null;
                }
                else return;
            }
            var go = Ui.Rect("Controls Panel", Interface.Interface.Root).Fill().gameObject;
            open = go.AddComponent<ControlsPanel>();
            open.simulation = Sandbox.Running;
            open.right = right;
            open.Build();
            if (!open.simulation) {
                var motors = MotorDrive.Live().Count;
                var cylinders = Controls.Cylinders().Count;
                Interface.Interface.Say(motors + cylinders == 0 ? "Controls: there are no motors or cylinders in the build yet"
                    : "Controls for " + motors + (motors == 1 ? " motor" : " motors") + " and " + cylinders + (cylinders == 1 ? " cylinder" : " cylinders"), true);
            }
        }

        public static void Close() {
            if (open == null) return;
            Destroy(open.gameObject);
            open = null;
        }

        public static void CloseSimulation() {
            if (open != null && open.simulation) Close();
        }

        private void Build() {
            window = Ui.Panel(transform, "Window", Theme.Panel, 10f).rectTransform;
            window.anchorMin = window.anchorMax = new Vector2(0f, 1f);
            window.pivot = new Vector2(0f, 1f);
            // In the build it sits beside the tool rail, under the tool's hints; in the sandbox those are hidden.
            window.anchoredPosition = simulation ? new Vector2(16f, -(Layout.TopBarBottom + 12f)) : new Vector2(76f, -(Layout.TopBarBottom + 60f));
            window.sizeDelta = new Vector2(450f, 0f);
            if (right) {   // V2: the game's setup (its panel is down the left)
                window.anchorMin = window.anchorMax = window.pivot = new Vector2(1f, 1f);
                window.anchoredPosition = new Vector2(-330f, -(Layout.TopBarBottom + 12f));
            }
            Ui.Column(window, 6f, new RectOffset(14, 14, 10, 14));
            window.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            window.GetChild(0).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            var header = Ui.Rect("Header", window);
            Ui.Row(header, 6f);
            header.Size(-1, 32f);
            title = Ui.Text(header, "Title", simulation ? "Simulation" : "Controls", Theme.FontTitle, Theme.Text, Theme.Medium);
            title.Size(-1, 30f, 1f);
            if (simulation) {
                var pause = Ui.Button(header, "Pause", "Pause", null, false, 5f, Theme.FontSmall);
                pause.onClick.AddListener(() => {
                    if (Sandbox.Instance == null) return;
                    Sandbox.Instance.SetPaused(!Sandbox.Instance.Paused);
                    pause.GetComponentInChildren<TextMeshProUGUI>().text = Sandbox.Instance.Paused ? "Run" : "Pause";
                });
                pause.Size(-1, 28f);
                Ui.Tip(pause, "Pause", null, "Stops time (\".\" steps it while paused).");
                var reset = Ui.Button(header, "Reset", "Reset", Sandbox.Reset, false, 5f, Theme.FontSmall);
                reset.Size(-1, 28f);
                Ui.Tip(reset, "Reset", null, "Starts the simulation again from the build.");
                var stop = Ui.Button(header, "Stop", "Stop", Sandbox.Stop, true, 5f, Theme.FontSmall);
                stop.Size(-1, 28f);
                Ui.Tip(stop, "Stop simulating", "Esc", "Back to building; the build is as it was.");
            }
            else {
                var close = Ui.Button(header, "Close", "×", Close, false, 5f, Theme.FontTitle);
                close.Size(28f, 28f);
            }
            body = Ui.Rect("Body", window);
            Ui.Column(body, 6f);
            body.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            status = Ui.Text(window, "Status", "", Theme.FontSmall, Theme.TextDim);
            status.textWrappingMode = TextWrappingModes.Normal;
            status.Size(-1, 34f);
        }

        private string Signature() => (Sandbox.Running ? "S" : "B") + Controls.Drive + "#" +
            string.Join("|", MotorDrive.Live().Select(m => m.Label + m.Rpm + Controls.ForwardKey(m.gameObject) + Controls.ReverseKey(m.gameObject) + Controls.Reversed(m.gameObject) + Controls.Side(m.gameObject)))
            + "#" + string.Join("|", Controls.Cylinders().Select(c => c.Label + Controls.CylinderKey(c.gameObject)));

        private void Rebuild() {
            for (var i = body.childCount - 1; i >= 0; i--) Destroy(body.GetChild(i).gameObject);
            var motors = MotorDrive.Live();
            var cylinders = Controls.Cylinders();

            // The drivetrain.
            Heading("Drivetrain");
            var mode = Ui.Rect("Mode", body);
            Ui.Row(mode, 8f);
            mode.Size(-1, 32f);
            ToolOptions.Segmented(mode, Controls.DriveModeNames, (int)Controls.Drive, i => {
                Controls.Drive = (Controls.DriveMode)i;
                Interface.Interface.Say("Drive: " + Controls.DriveModeNames[i] + ". " + Controls.DriveHelp(Controls.Drive), true);
                Audio.Sounds.Play(Audio.Sounds.Tick);
            });
            Note(Controls.DriveHelp(Controls.Drive) + (Gamepad.current != null ? ". A gamepad is connected: its sticks drive too." : "."));
            var sides = motors.Count(m => Controls.Side(m.gameObject) != "");
            if (motors.Count > 0 && sides == 0) Note("Set motors to the left (L) or right (R) side below to drive with them. If a side drives backwards, reverse its motors.");
            if (simulation && Sandbox.Instance != null)
                ToolOptions.Switch(body, "Camera follows the robot", Sandbox.Instance.Follow, on => {
                    if (Sandbox.Instance != null) Sandbox.Instance.Follow = on;
                    Interface.Interface.Say(on ? "The camera follows the robot" : "The camera stays put", true);
                });

            // Motors.
            Heading(motors.Count == 0 ? "Motors: none in the build" : "Motors");
            foreach (var motor in motors) MotorRow(motor);
            // Cylinders.
            Heading(cylinders.Count == 0 ? "Cylinders: none in the build" : "Cylinders");
            foreach (var cylinder in cylinders) CylinderRow(cylinder);
            if (motors.Count + cylinders.Count > 0)
                Note("Several motors on one key run together. Click a motor in the build for its speed, gearbox and name. Hover here to see which is which.");
        }

        private void MotorRow(MotorDrive motor) {
            var part = motor.gameObject;
            var row = Ui.Rect(motor.Label, body);
            Ui.Row(row, 6f);
            row.Size(-1, 30f);
            var dot = Ui.Image(row, "Cartridge", motor.Color, 5f);
            dot.Size(10f, 10f);
            var name = Ui.Text(row, "Name", motor.Label, Theme.FontBody, Theme.Text);
            name.Size(96f, 28f);
            Ui.Tip(name, motor.Label, null, motor.Kind + " motor, " + motor.Rpm + " rpm (" + (motor.HasCartridge ? MotorDrive.CartridgeName(motor.Rpm) + " cartridge" : "fixed") + ")");
            // Side of the drivetrain.
            var side = Controls.Side(part);
            foreach (var (value, label, tip) in new[] { ("", "-", "Not on the drivetrain: it runs from its keys."), ("L", "L", "The drivetrain's left side."), ("R", "R", "The drivetrain's right side.") }) {
                var on = side == value;
                var chip = Ui.Button(row, "Side " + label, label, () => {
                    Controls.SetSide(part, value);
                    Interface.Interface.Say(motor.Label + (value == "" ? " runs from its keys" : value == "L" ? " drives the left side" : " drives the right side"), true);
                    Audio.Sounds.Play(Audio.Sounds.Tick);
                }, on, 5f, Theme.FontSmall);
                chip.Size(28f, 26f);
                if (!on) chip.targetGraphic.color = Theme.Field;
                Ui.Tip(chip, label == "-" ? "Keys" : label == "L" ? "Left side" : "Right side", null, tip);
            }
            if (side == "") {
                MotionUi.KeyChip(row, Controls.ForwardKey(part), key => SetKey(motor, key, true), "Forward key", "Hold it to run " + motor.Label + " forward.");
                MotionUi.KeyChip(row, Controls.ReverseKey(part), key => SetKey(motor, key, false), "Reverse key", "Hold it to run " + motor.Label + " in reverse.");
            }
            else {
                var drive = Ui.Text(row, "Drive", side == "L" ? "drives left" : "drives right", Theme.FontSmall, Theme.TextDim);
                drive.Size(106f, 26f);
            }
            var reversed = Controls.Reversed(part);
            var rev = Ui.Button(row, "Reversed", "Rev", () => {
                Controls.SetReversed(part, !Controls.Reversed(part));
                Interface.Interface.Say(motor.Label + (Controls.Reversed(part) ? " reversed: forward turns the other way" : " not reversed"), true);
                Audio.Sounds.Play(Audio.Sounds.Tick);
            }, reversed, 5f, Theme.FontSmall);
            rev.Size(-1, 26f);
            if (!reversed) rev.targetGraphic.color = Theme.Field;
            Ui.Tip(rev, "Reversed", null, "Forward turns the other way (motors on the two sides of a drive face opposite ways).");
            if (simulation && Sandbox.Instance != null && !Sandbox.Instance.Drives(motor)) {
                var none = Ui.Text(row, "Idle", "no shaft", Theme.FontSmall, Theme.Warning);
                none.Size(-1, 26f);
            }
        }

        private void CylinderRow(PneumaticCylinder cylinder) {
            var row = Ui.Rect(cylinder.Label, body);
            Ui.Row(row, 6f);
            row.Size(-1, 30f);
            var dot = Ui.Image(row, "Mark", Theme.Warning, 5f);
            dot.Size(10f, 10f);
            var name = Ui.Text(row, "Name", cylinder.Label, Theme.FontBody, Theme.Text);
            name.Size(96f, 28f);
            var stroke = Ui.Text(row, "Stroke", cylinder.Size + " stroke", Theme.FontSmall, Theme.TextDim);
            stroke.Size(90f, 26f);
            MotionUi.KeyChip(row, Controls.CylinderKey(cylinder.gameObject), key => {
                Controls.SetCylinderKey(cylinder.gameObject, key);
                Interface.Interface.Say(cylinder.Label + ": " + (key == Key.None ? "no key" : Controls.KeyName(key) + " extends and retracts it"), true);
                Audio.Sounds.Play(Audio.Sounds.Tick);
            }, "Key", "Press it to extend " + cylinder.Label + ", again to retract it.");
            if (simulation && Sandbox.Instance != null && !Sandbox.Instance.Slides(cylinder)) {
                var stuck = Ui.Text(row, "Stuck", "held still", Theme.FontSmall, Theme.Warning);
                stuck.Size(-1, 26f);
                Ui.Tip(stuck, "Held still", null, "What's on its rod is also fastened to what its body is on, so it can't move.");
            }
        }

        private static void SetKey(MotorDrive motor, Key key, bool forward) {
            if (forward) Controls.SetForwardKey(motor.gameObject, key);
            else Controls.SetReverseKey(motor.gameObject, key);
            var warn = Controls.DriveKey(key) ? " (the drive uses it too)" : "";
            Interface.Interface.Say(motor.Label + (forward ? " forward: " : " reverse: ") + (key == Key.None ? "no key" : "hold " + Controls.KeyName(key)) + warn, warn == "");
            Audio.Sounds.Play(Audio.Sounds.Tick);
        }

        private void Heading(string text) {
            var heading = Ui.Text(body, "Heading", text, Theme.FontSmall, Theme.Text, Theme.Medium);
            heading.Size(-1, 24f);
        }

        private void Note(string text) {
            var note = Ui.Text(body, "Note", text, Theme.FontSmall, Theme.TextDim);
            note.textWrappingMode = TextWrappingModes.Normal;
            note.Size(-1, 34f);
        }

        private void Update() {
            if (simulation && !Sandbox.Running) {
                Close();
                return;
            }
            var signature = Signature();
            if (signature != shown && !MotionUi.Capturing) {
                shown = signature;
                Rebuild();
            }
            if (simulation && Sandbox.Instance != null) {
                var (left, right) = Sandbox.DrivePower();
                status.text = Sandbox.Instance.BodyCount + " bodies · drive " + Mathf.RoundToInt(left * 100f) + "% / " + Mathf.RoundToInt(right * 100f) + "%"
                              + (Sandbox.Instance.Paused ? " · paused" : "");
            }
            else status.text = "Edit > Controls. The keys run selected motors in the build, and every motor in the simulation.";
            if (RectTransformUtility.RectangleContainsScreenPoint(window, MouseInput.Position, null))
                MotionUi.DrawLabels(MotorDrive.Live(), Controls.Cylinders());
        }
    }
}
