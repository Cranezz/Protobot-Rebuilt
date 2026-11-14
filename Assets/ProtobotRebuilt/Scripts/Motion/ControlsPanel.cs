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
            window.sizeDelta = new Vector2(560f, 0f);
            if (right) {   // V2: the game's setup (its panel is down the left)
                window.anchorMin = window.anchorMax = window.pivot = new Vector2(1f, 1f);
                window.anchoredPosition = new Vector2(-330f, -(Layout.TopBarBottom + 12f));
            }
            Ui.Column(window, 6f, new RectOffset(16, 16, 12, 14));
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
            Ui.Column(body, 5f);
            body.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            status = Ui.Text(window, "Status", "", Theme.FontSmall, Theme.TextDim);
            status.textWrappingMode = TextWrappingModes.Normal;
            status.Size(-1, 36f);
        }

        private string Signature() => (Sandbox.Running ? "S" : "B") + Controls.Drive + "#" +
            string.Join("|", MotorDrive.Live().Select(m => m.Label + m.Rpm + Controls.ForwardKey(m.gameObject) + Controls.ReverseKey(m.gameObject) + Controls.Reversed(m.gameObject) + Controls.Side(m.gameObject)))
            + "#" + string.Join("|", Controls.Cylinders().Select(c => c.Label + Controls.CylinderKey(c.gameObject)));

        // V2: laid out like the app's other windows: headed sections, each motor a card on one line (its cartridge
        // color, which you can change; its name; Keys / Left / Right; its keys or the side it drives; Normal / Reversed).
        private void Rebuild() {
            for (var i = body.childCount - 1; i >= 0; i--) Destroy(body.GetChild(i).gameObject);
            var motors = MotorDrive.Live();
            var cylinders = Controls.Cylinders();

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
            if (simulation && Sandbox.Instance != null) {
                Heading("Camera");
                var cameraRow = Ui.Rect("Camera", body);
                Ui.Row(cameraRow, 8f);
                cameraRow.Size(-1, 32f);
                ToolOptions.Segmented(cameraRow, new[] { "Free", "Follow robot", "Track a part" }, (int)Sandbox.Instance.View, i => {
                    if (Sandbox.Instance != null) Sandbox.Instance.SetCamera((Sandbox.CameraMode)i);
                    shown = null;
                });
                Note("Free: orbit, pan and zoom anywhere. Track a part: click a part of the robot or a game piece.");
            }

            var drive = motors.Where(m => Controls.Side(m.gameObject) != "").OrderBy(m => Controls.Side(m.gameObject)).ThenBy(m => m.Label).ToList();
            var others = motors.Where(m => Controls.Side(m.gameObject) == "").ToList();
            if (motors.Count == 0) Heading("Motors: none in the build");
            else {
                Heading(drive.Count == 0 ? "Drive motors: none yet" : "Drive motors");
                if (drive.Count == 0) Note("Set motors to Left or Right to drive with them. If a side drives backwards, reverse its motors.");
                foreach (var motor in drive) MotorRow(motor);
                if (others.Count > 0) {
                    Heading("Other motors");
                    foreach (var motor in others) MotorRow(motor);
                }
            }
            Heading(cylinders.Count == 0 ? "Cylinders: none in the build" : "Cylinders");
            foreach (var cylinder in cylinders) CylinderRow(cylinder);
            if (motors.Count + cylinders.Count > 0)
                Note("Motors on one key run together. Click a motor in the build for its speed and name; hover here to see which is which.");
        }

        private void MotorRow(MotorDrive motor) {
            var part = motor.gameObject;
            var card = Ui.Image(body, motor.Label, Theme.PanelRaised, 8f);
            var row = card.rectTransform;
            Ui.Row(row, 8f, new RectOffset(10, 8, 0, 0));
            row.Size(-1, 40f);

            // The cartridge: its color; click to change it.
            var swatch = Ui.Image(row, "Cartridge", motor.HasCartridge ? motor.Color : Theme.TextFaint, 0f, true);
            swatch.sprite = Theme.Circle;
            swatch.Size(18f, 18f);
            Ui.Tip(swatch, motor.HasCartridge ? "Cartridge" : "5.5W motor", null, motor.HasCartridge
                ? MotorDrive.CartridgeName(motor.Rpm) + ", " + motor.Rpm + " rpm. Click to change it: red 100 rpm (arms), green 200 (drives), blue 600 (flywheels, intakes)."
                : "The 5.5W motor has no cartridge: it turns at 200 rpm.");
            if (motor.HasCartridge) {
                var pick = swatch.gameObject.AddComponent<Button>();
                pick.transition = Selectable.Transition.None;
                var rect = swatch.rectTransform;
                pick.onClick.AddListener(() => {
                    var labels = MotorDrive.Cartridges.Select(r => char.ToUpper(MotorDrive.CartridgeName(r)[0]) + MotorDrive.CartridgeName(r).Substring(1) + " · " + r + " rpm").ToArray();
                    Popup.Open(rect, labels, System.Array.IndexOf(MotorDrive.Cartridges, motor.Rpm), i => {
                        motor.SetCartridge(MotorDrive.Cartridges[i], false);
                        Interface.Interface.Say(motor.Label + ": " + MotorDrive.CartridgeName(MotorDrive.Cartridges[i]) + " cartridge, " + MotorDrive.Cartridges[i] + " rpm", true);
                        Audio.Sounds.Play(Audio.Sounds.Tick);
                    });
                });
            }

            var name = Ui.Text(row, "Name", motor.Label, Theme.FontBody, Theme.Text);
            name.overflowMode = TextOverflowModes.Ellipsis;
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.Size(-1, 40f, 1f);
            if (simulation && Sandbox.Instance != null && !Sandbox.Instance.Drives(motor)) {
                name.text = motor.Label + "  <color=#E8B04A><size=80%>no shaft</size></color>";
                Ui.Tip(name, motor.Label, null, "Nothing is in its square hole: it turns nothing.");
            }

            // Keys, or a side of the drivetrain.
            var side = Controls.Side(part);
            var roleHolder = Ui.Rect("Role", row);
            Ui.Row(roleHolder, 0f);
            roleHolder.Size(150f, 30f);
            ToolOptions.Segmented(roleHolder, new[] { "Keys", "Left", "Right" }, side == "L" ? 1 : side == "R" ? 2 : 0, i => {
                var value = i == 1 ? "L" : i == 2 ? "R" : "";
                Controls.SetSide(part, value);
                Interface.Interface.Say(motor.Label + (value == "" ? " runs from its keys" : value == "L" ? " drives the left side" : " drives the right side"), true);
                Audio.Sounds.Play(Audio.Sounds.Tick);
            });
            Ui.Tip(roleHolder.GetComponentInChildren<Button>(), "What runs it", null, "Keys: its own forward and reverse keys. Left / Right: a side of the drivetrain (the sticks drive it).");

            var keys = Ui.Rect("Keys", row);
            Ui.Row(keys, 4f);
            keys.Size(100f, 30f);
            if (side == "") {
                MotionUi.KeyChip(keys, Controls.ForwardKey(part), key => SetKey(motor, key, true), "Forward key", "Hold it to run " + motor.Label + " forward.").Size(46f, 26f);
                MotionUi.KeyChip(keys, Controls.ReverseKey(part), key => SetKey(motor, key, false), "Reverse key", "Hold it to run " + motor.Label + " in reverse.").Size(46f, 26f);
            }
            else Ui.Text(keys, "Drive", side == "L" ? "left stick" : "right stick", Theme.FontSmall, Theme.TextDim, null, TextAlignmentOptions.Center).Size(100f, 30f);

            var reversed = Controls.Reversed(part);
            var rev = Ui.Button(row, "Reversed", reversed ? "Reversed" : "Normal", () => {
                Controls.SetReversed(part, !Controls.Reversed(part));
                Interface.Interface.Say(motor.Label + (Controls.Reversed(part) ? " reversed: forward turns the other way" : " turns the normal way"), true);
                Audio.Sounds.Play(Audio.Sounds.Tick);
            }, reversed, 5f, Theme.FontSmall);
            rev.Size(80f, 28f);
            if (!reversed) {
                rev.targetGraphic.color = Theme.Field;
                rev.GetComponentInChildren<TextMeshProUGUI>().color = Theme.TextDim;
            }
            Ui.Tip(rev, "Direction", null, "Reversed: forward turns this motor the other way (the two sides of a drive face opposite ways).");
        }

        private void CylinderRow(PneumaticCylinder cylinder) {
            var card = Ui.Image(body, cylinder.Label, Theme.PanelRaised, 8f);
            var row = card.rectTransform;
            Ui.Row(row, 8f, new RectOffset(10, 8, 0, 0));
            row.Size(-1, 58f);
            var dot = Ui.Image(row, "Mark", Theme.Warning, 0f);
            dot.sprite = Theme.Circle;
            dot.Size(18f, 18f);
            var name = Ui.Text(row, "Name", cylinder.Label, Theme.FontBody, Theme.Text);
            name.Size(-1, 40f, 1f);
            var stroke = Ui.Text(row, "Stroke", cylinder.Size + " stroke", Theme.FontSmall, Theme.TextDim);
            stroke.Size(90f, 26f);
            MotionUi.KeyChip(row, Controls.CylinderKey(cylinder.gameObject), key => {
                Controls.SetCylinderKey(cylinder.gameObject, key);
                Interface.Interface.Say(cylinder.Label + ": " + (key == Key.None ? "no key" : Controls.KeyName(key) + " " + PneumaticCylinder.KeyAction(cylinder.Ports)), true);
                Audio.Sounds.Play(Audio.Sounds.Tick);
            }, "Key", "Press it: " + PneumaticCylinder.KeyAction(cylinder.Ports) + ".");
            // V2: which ends get air (one key does it all), on a picture of the cylinder: click a fitting to connect
            // or disconnect its air.
            var picker = AirPicker.Create(row, cylinder, () => {
                if (simulation && Sandbox.Instance != null) Sandbox.Instance.SetPorts(cylinder);
                Rebuild();
            }, 150f, 50f);
            picker.GetComponent<LayoutElement>().flexibleWidth = 0f;
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
            var heading = Dialog.Heading(body, text);
            heading.margin = new Vector4(0, 8, 0, 0);
            heading.Size(-1, 28f);
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
            else status.text = "In the build the keys run the selected motors; in the simulation, every motor.";
            if (RectTransformUtility.RectangleContainsScreenPoint(window, MouseInput.Position, null))
                MotionUi.DrawLabels(MotorDrive.Live(), Controls.Cylinders());
        }
    }
}
