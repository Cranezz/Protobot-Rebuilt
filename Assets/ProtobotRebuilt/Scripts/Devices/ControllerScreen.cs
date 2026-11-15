using System;
using System.Collections.Generic;
using System.Linq;
using Protobot.Interface;
using Protobot.Motion;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Devices {
    /// <summary>
    /// V2 (Phase 7): the controller screen (Edit > Controller…, the Devices window's Controller tab). A V5 controller
    /// drawn on screen, each control showing its key; click one and press a key or a gamepad control to bind it (like a
    /// game's controller settings). Beside it, the chosen control: its key and gamepad control (change or clear each),
    /// what it runs on this robot (the drive, by the drive mode; the motors and cylinders on its key) and "Put on this
    /// button" for any motor or cylinder. Presets on top: Tank, Arcade, Split arcade. The bindings are this robot's
    /// (saved in its build: V5Controller); Use as my default makes them the default for robots without their own.
    /// Controls light up while held, and the sticks show where they are, so a gamepad can be tried right here.
    /// </summary>
    public class ControllerScreen : MonoBehaviour {
        private static ControllerScreen open;
        private Dialog dialog;
        private RectTransform pad, details;
        private readonly Dictionary<string, (Image image, TextMeshProUGUI key)> controls = new();
        private RectTransform leftDot, rightDot;
        private string selected = "R1";
        private enum Capture { None, Key, Pad }
        private Capture capture;
        private TextMeshProUGUI captureNote;
        private float captureSince;

        private static readonly Color Body = new(0.13f, 0.14f, 0.16f, 1f);
        private static readonly Color Control = new(0.22f, 0.235f, 0.27f, 1f);
        private static readonly Color Lit = new(0.157f, 0.522f, 0.957f, 1f);

        public static void Open() {
            if (open != null) return;
            var dialog = Dialog.Open("Controller", new Vector2(1060f, 680f));
            var screen = dialog.gameObject.AddComponent<ControllerScreen>();
            open = screen;
            screen.dialog = dialog;
            screen.Build();
            V5Controller.Changed += screen.Refresh;
            dialog.OnClose += () => {
                V5Controller.Changed -= screen.Refresh;
                if (open == screen) open = null;
            };
        }

        private void Build() {
            var body = dialog.Body;
            // Presets.
            var top = Ui.Rect("Presets", body).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(1020f, 34f));
            Ui.Row(top, 10f);
            Ui.Text(top, "Label", "Drive", Theme.FontBody, Theme.TextDim, Theme.Medium).Size(52f, 32f);
            ToolOptions.Segmented(top, Controls.DriveModeNames, (int)Controls.Drive, i => {
                Controls.Drive = (Controls.DriveMode)i;
                Audio.Sounds.Play(Audio.Sounds.Tick);
                Refresh();
            });
            var help = Ui.Text(top, "Help", "", Theme.FontSmall, Theme.TextDim);
            help.Size(-1, 32f, 1f);
            help.name = "DriveHelp";

            // The controller.
            var stage = Ui.Image(body, "Stage", new Color(0f, 0f, 0f, 0.18f), 12f);
            stage.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -46f), new Vector2(660f, 500f));
            pad = Ui.Rect("Pad", stage.transform).Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(620f, 440f));
            DrawController();

            // The chosen control.
            var side = Ui.Image(body, "Details", Theme.PanelRaised, 10f);
            side.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -46f), new Vector2(344f, 500f));
            details = Ui.Rect("Content", side.transform).Fill(16, 14, 16, 14);
            Ui.Column(details, 8f);

            captureNote = Ui.Text(body, "Capture", "", Theme.FontSmall, Theme.Warning, Theme.Medium, TextAlignmentOptions.Center);
            captureNote.rectTransform.Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 6f), new Vector2(660f, 24f));

            var reset = Ui.Button(dialog.Footer, "Reset", "Reset to default", () => {
                V5Controller.ResetToDefault();
                Audio.Sounds.Play(Audio.Sounds.Tick);
            }, false, 6f);
            reset.Size(160f, 34f);
            Ui.Tip(reset, "Reset to default", null, "This robot goes back to your default bindings (or Protobot's).");
            var mine = Ui.Button(dialog.Footer, "Default", "Use as my default", () => {
                V5Controller.MakeDefault();
                Interface.Interface.Announce("These bindings are now the default for robots without their own");
            }, false, 6f);
            mine.Size(170f, 34f);
            Ui.Tip(mine, "Use as my default", null, "Robots that don't have their own bindings use these.");
            var done = Ui.Button(dialog.Footer, "Done", "Done", dialog.Close, true, 6f);
            done.Size(110f, 34f);
            Refresh();
        }

        // ------------------------------------------------------------------ drawing

        private void DrawController() {
            // Shoulder buttons, then the body with its grips.
            Element("L1", new Vector2(-205f, 196f), new Vector2(130f, 30f), false);
            Element("L2", new Vector2(-205f, 162f), new Vector2(130f, 30f), false);
            Element("R1", new Vector2(205f, 196f), new Vector2(130f, 30f), false);
            Element("R2", new Vector2(205f, 162f), new Vector2(130f, 30f), false);
            var shell = Ui.Image(pad, "Body", Body, 40f);
            shell.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(600f, 270f));
            foreach (var x in new[] { -200f, 200f }) {
                var grip = Ui.Image(pad, "Grip", Body);
                grip.sprite = Theme.Circle;
                grip.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, -110f), new Vector2(190f, 190f));
                grip.transform.SetSiblingIndex(shell.transform.GetSiblingIndex());
            }
            var logo = Ui.Text(pad, "Logo", "V5", 22f, new Color(1f, 1f, 1f, 0.25f), Theme.Bold, TextAlignmentOptions.Center);
            logo.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(80f, 30f));
            // Sticks, each with its four directions.
            leftDot = Stick(new Vector2(-150f, 58f), "Axis3+", "Axis3-", "Axis4-", "Axis4+", "3", "4");
            rightDot = Stick(new Vector2(150f, 58f), "Axis2+", "Axis2-", "Axis1-", "Axis1+", "2", "1");
            // D-pad (arrows) and the X/B/Y/A buttons.
            var d = new Vector2(-195f, -122f);
            Element("Up", d + new Vector2(0f, 36f), new Vector2(46f, 34f), false);
            Element("Down", d + new Vector2(0f, -36f), new Vector2(46f, 34f), false);
            Element("Left", d + new Vector2(-46f, 0f), new Vector2(46f, 34f), false);
            Element("Right", d + new Vector2(46f, 0f), new Vector2(46f, 34f), false);
            var f = new Vector2(195f, -122f);
            Element("X", f + new Vector2(0f, 40f), new Vector2(46f, 46f), true);
            Element("B", f + new Vector2(0f, -40f), new Vector2(46f, 46f), true);
            Element("Y", f + new Vector2(-44f, 0f), new Vector2(46f, 46f), true);
            Element("A", f + new Vector2(44f, 0f), new Vector2(46f, 46f), true);
        }

        private RectTransform Stick(Vector2 at, string up, string down, string left, string right, string vertical, string across) {
            var ring = Ui.Image(pad, "Stick", new Color(0.08f, 0.085f, 0.1f, 1f));
            ring.sprite = Theme.Circle;
            ring.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), at, new Vector2(96f, 96f));
            var dot = Ui.Image(ring.transform, "Dot", new Color(0.32f, 0.34f, 0.38f, 1f));
            dot.sprite = Theme.Circle;
            dot.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(44f, 44f));
            var axes = Ui.Text(pad, "Axes", "Axis " + vertical + " up/down, " + across + " across", 10.5f, new Color(1f, 1f, 1f, 0.35f), null, TextAlignmentOptions.Center);
            axes.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), at + new Vector2(0f, -106f), new Vector2(170f, 16f));
            Element(up, at + new Vector2(0f, 72f), new Vector2(58f, 30f), false);
            Element(down, at + new Vector2(0f, -78f), new Vector2(58f, 30f), false);
            Element(left, at + new Vector2(-84f, 0f), new Vector2(58f, 30f), false);
            Element(right, at + new Vector2(84f, 0f), new Vector2(58f, 30f), false);
            return dot.rectTransform;
        }

        private void Element(string id, Vector2 at, Vector2 size, bool round) {
            var image = Ui.Image(pad, id, Control, round ? 0f : 8f, true);
            if (round) image.sprite = Theme.Circle;
            image.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), at, size);
            var button = image.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            var nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;
            button.onClick.AddListener(() => {
                selected = id;
                capture = Capture.Key;   // clicking a control binds it: press a key or a gamepad control
                captureSince = Time.unscaledTime;
                Audio.Sounds.Play(Audio.Sounds.Tick);
                Refresh();
            });
            var label = id.StartsWith("Axis") ? "" : id;
            var name = Ui.Text(image.transform, "Name", label, 10f, new Color(1f, 1f, 1f, 0.5f), Theme.Medium, TextAlignmentOptions.Top);
            name.rectTransform.Fill(0, 2, 0, 0);
            var key = Ui.Text(image.transform, "Key", "", 13f, Color.white, Theme.Bold, label.Length > 0 ? TextAlignmentOptions.Bottom : TextAlignmentOptions.Center);
            key.rectTransform.Fill(2, 0, 2, label.Length > 0 ? 3 : 0);
            key.overflowMode = TextOverflowModes.Ellipsis;
            Ui.Tip(image, V5Controller.SlotName(id), null, "Click, then press a key or a gamepad control to bind it.");
            controls[id] = (image, key);
        }

        // ------------------------------------------------------------------ the chosen control

        private void Refresh() {
            if (this == null) return;
            var help = dialog.Body.Find("Presets/DriveHelp")?.GetComponent<TextMeshProUGUI>();
            if (help != null) help.text = Controls.DriveHelp(Controls.Drive).Replace(" (W/S)", "").Replace(" (Up/Down)", "").Replace(" (W/A/S/D)", "").Replace(" (Left/Right)", "");
            foreach (var (id, (_, key)) in controls) key.text = V5Controller.KeyLabel(id);
            foreach (Transform child in details) Destroy(child.gameObject);
            details.DetachChildren();

            var title = Ui.Text(details, "Title", V5Controller.SlotName(selected), Theme.FontTitle + 2f, Theme.Text, Theme.Medium);
            title.Size(-1, 30f);
            Line("Key", V5Controller.KeyLabel(selected), "Change", () => Begin(Capture.Key), () => V5Controller.Set(selected, Key.None, null));
            Line("Gamepad", V5Controller.PadLabel(selected), "Change", () => Begin(Capture.Pad), () => V5Controller.Set(selected, null, ""));
            var source = Ui.Text(details, "Source", V5Controller.HasOwn ? "This robot's bindings (saved with the build)" : "Your default bindings (change one and this robot gets its own)",
                Theme.FontSmall, Theme.TextFaint);
            source.textWrappingMode = TextWrappingModes.Normal;
            source.Size(-1, 34f);

            Dialog.Heading(details, "Runs").Size(-1, 24f);
            foreach (var line in Runs(selected)) {
                var t = Ui.Text(details, "Run", line, Theme.FontSmall, Theme.Text);
                t.textWrappingMode = TextWrappingModes.Normal;
                t.Size(-1, 20f);
            }
            if (!selected.StartsWith("Axis")) {
                var functions = Functions();
                if (functions.Count > 0) {
                    var row = Ui.Rect("Put", details);
                    Ui.Row(row, 8f);
                    row.Size(-1, 32f);
                    Ui.Text(row, "Label", "Put on it", Theme.FontSmall, Theme.TextDim).Size(70f, 30f);
                    var names = new[] { "Choose…" }.Concat(functions.Select(f => f.name)).ToArray();
                    Popup.Choice(row, names, 0, i => {
                        if (i == 0) return;
                        var key = V5Controller.KeyOf(selected);
                        if (key == Key.None) {
                            Interface.Interface.Say("Give " + selected + " a key first (Change)", false);
                            return;
                        }
                        functions[i - 1].put(key);
                        Audio.Sounds.Play(Audio.Sounds.Tick);
                        Refresh();
                    }, 220f);
                }
            }
            var hint = Ui.Text(details, "Hint", "Click a control on the controller, then press a key or a gamepad control for it. Esc cancels, Delete clears it.",
                Theme.FontSmall, Theme.TextFaint);
            hint.textWrappingMode = TextWrappingModes.Normal;
            hint.Size(-1, 52f);
            UpdateCaptureNote();
        }

        private void Line(string label, string value, string action, Action change, Action clear) {
            var row = Ui.Rect(label, details);
            Ui.Row(row, 8f);
            row.Size(-1, 32f);
            Ui.Text(row, "Label", label, Theme.FontSmall, Theme.TextDim).Size(70f, 30f);
            Ui.Text(row, "Value", value, Theme.FontBody, Theme.Text, Theme.Bold).Size(-1, 30f, 1f);
            var changeButton = Ui.Button(row, "Change", action, () => change(), false, 5f, Theme.FontSmall);
            changeButton.Size(70f, 28f);
            var clearButton = Ui.Button(row, "Clear", "×", () => clear(), false, 5f, Theme.FontBody);
            clearButton.Size(30f, 28f);
            Ui.Tip(clearButton, "Clear", null, "Nothing on it.");
        }

        private void Begin(Capture what) {
            capture = what;
            captureSince = Time.unscaledTime;
            UpdateCaptureNote();
        }

        private void UpdateCaptureNote() {
            if (captureNote == null) return;
            captureNote.text = capture switch {
                Capture.Key => "Press a key (or a gamepad control) for " + V5Controller.SlotName(selected) + "…  Esc cancels",
                Capture.Pad => "Press a gamepad control for " + V5Controller.SlotName(selected) + "…  Esc cancels",
                _ => "",
            };
            dialog.closeOnEscape = capture == Capture.None;
        }

        /// <summary>What a control runs on this robot.</summary>
        private static List<string> Runs(string id) {
            var lines = new List<string>();
            var mode = Controls.Drive;
            string Drive(string text) => "Drive: " + text + " (" + Controls.DriveModeNames[(int)mode] + ")";
            switch (id) {
                case "Axis3+": lines.Add(Drive(mode == Controls.DriveMode.Tank ? "left side forward" : "forward")); break;
                case "Axis3-": lines.Add(Drive(mode == Controls.DriveMode.Tank ? "left side back" : "back")); break;
                case "Axis4+": if (mode == Controls.DriveMode.Arcade) lines.Add(Drive("turn right")); break;
                case "Axis4-": if (mode == Controls.DriveMode.Arcade) lines.Add(Drive("turn left")); break;
                case "Axis2+": if (mode == Controls.DriveMode.Tank) lines.Add(Drive("right side forward")); break;
                case "Axis2-": if (mode == Controls.DriveMode.Tank) lines.Add(Drive("right side back")); break;
                case "Axis1+": if (mode == Controls.DriveMode.SplitArcade) lines.Add(Drive("turn right")); break;
                case "Axis1-": if (mode == Controls.DriveMode.SplitArcade) lines.Add(Drive("turn left")); break;
            }
            var key = V5Controller.KeyOf(id);
            if (key != Key.None) {
                foreach (var motor in MotorDrive.Live()) {
                    if (Controls.Side(motor.gameObject) != "") continue;
                    if (Controls.ForwardKey(motor.gameObject) == key) lines.Add(motor.Label + ": forward");
                    if (Controls.ReverseKey(motor.gameObject) == key) lines.Add(motor.Label + ": reverse");
                }
                foreach (var cylinder in Controls.Cylinders())
                    if (Controls.CylinderKey(cylinder.gameObject) == key) lines.Add((Controls.GivenName(cylinder.gameObject) ?? "Cylinder") + ": out / in");
            }
            if (lines.Count == 0) lines.Add(id.StartsWith("Axis") ? "Nothing in this drive mode (robot code can read it)" : "Nothing yet (robot code can read it)");
            return lines;
        }

        /// <summary>The motors and cylinders that can go on a button: a name, and how to put it on a key.</summary>
        private static List<(string name, Action<Key> put)> Functions() {
            var list = new List<(string, Action<Key>)>();
            foreach (var motor in MotorDrive.Live()) {
                if (Controls.Side(motor.gameObject) != "") continue;
                var m = motor;
                list.Add((m.Label + " forward", k => Controls.SetForwardKey(m.gameObject, k)));
                list.Add((m.Label + " reverse", k => Controls.SetReverseKey(m.gameObject, k)));
            }
            foreach (var cylinder in Controls.Cylinders()) {
                var c = cylinder;
                list.Add(((Controls.GivenName(c.gameObject) ?? "Cylinder") + " out / in", k => Controls.SetCylinderKey(c.gameObject, k)));
            }
            return list;
        }

        // ------------------------------------------------------------------ live

        private void Update() {
            // Held controls light up; the sticks show where they are.
            foreach (var (id, (image, _)) in controls)
                image.color = id == selected ? (V5Controller.Held(id) ? Lit : new Color(0.3f, 0.33f, 0.4f, 1f)) : V5Controller.Held(id) ? Lit : Control;
            if (leftDot != null) leftDot.anchoredPosition = new Vector2(V5Controller.Axis(V5Axis.Axis4), V5Controller.Axis(V5Axis.Axis3)) * 22f;
            if (rightDot != null) rightDot.anchoredPosition = new Vector2(V5Controller.Axis(V5Axis.Axis1), V5Controller.Axis(V5Axis.Axis2)) * 22f;

            if (capture == Capture.None || Time.unscaledTime - captureSince < 0.15f) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) {
                capture = Capture.None;
                UpdateCaptureNote();
                return;
            }
            if (keyboard != null && (keyboard.deleteKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame)) {
                V5Controller.Set(selected, capture == Capture.Key ? Key.None : (Key?)null, capture == Capture.Pad ? "" : null);
                capture = Capture.None;
                UpdateCaptureNote();
                return;
            }
            var padControl = V5Controller.PressedPadControl();
            if (padControl != null) {
                V5Controller.Set(selected, null, padControl);
                capture = Capture.None;
                Audio.Sounds.Play(Audio.Sounds.Tick);
                UpdateCaptureNote();
                return;
            }
            if (capture == Capture.Key) {
                var key = Controls.PressedKey();
                if (key != Key.None && key != Key.Escape) {
                    V5Controller.Set(selected, key, null);
                    capture = Capture.None;
                    Audio.Sounds.Play(Audio.Sounds.Tick);
                    UpdateCaptureNote();
                }
            }
        }
    }
}
