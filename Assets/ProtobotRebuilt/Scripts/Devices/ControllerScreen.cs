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
    /// V2 (Phase 7): the controller screen (Edit > Controller…, the Devices window's Controller tab). VEX's picture of
    /// the V5 Controller with a row for each of its controls down each side and a line from the row to the control, as
    /// Steam's controller settings do; each row shows its key and gamepad control. Click one and press a key or a gamepad
    /// control to bind it. Beside it, the chosen control: its key and gamepad control (change or clear each),
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
        private TextMeshProUGUI status;   // V2
        private Image statusDot;
        private string shownDevice;
        private float captureSince;

        private static readonly Color Control = new(0.22f, 0.235f, 0.27f, 1f);
        private static readonly Color Lit = new(0.157f, 0.522f, 0.957f, 1f);

        public static void Open() {
            if (open != null) return;
            var dialog = Dialog.Open("Controller", new Vector2(1240f, 700f));
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
            var top = Ui.Rect("Presets", body).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(1200f, 34f));
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
            // V2: whether a gamepad or joystick is plugged in, and which.
            var holder = Ui.Rect("Connected", top).Size(10f, 10f);   // a rounded Image would ask the row for 66 px
            statusDot = Ui.Image(holder, "Dot", Theme.TextFaint, 5f);
            statusDot.rectTransform.Fill(0, 0, 0, 0);
            status = Ui.Text(top, "Controller", "", Theme.FontSmall, Theme.TextDim, Theme.Medium).Size(-1, 32f);
            shownDevice = "?";

            // The controller.
            var stage = Ui.Image(body, "Stage", new Color(0f, 0f, 0f, 0.18f), 12f);
            stage.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -46f), new Vector2(836f, 520f));
            pad = Ui.Rect("Pad", stage.transform).Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(836f, 520f));
            DrawController();

            // The chosen control.
            var side = Ui.Image(body, "Details", Theme.PanelRaised, 10f);
            side.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -46f), new Vector2(350f, 520f));
            details = Ui.Rect("Content", side.transform).Fill(16, 14, 16, 14);
            Ui.Column(details, 8f);

            captureNote = Ui.Text(body, "Capture", "", Theme.FontSmall, Theme.Warning, Theme.Medium, TextAlignmentOptions.Center);
            captureNote.rectTransform.Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(836f, 24f));

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

        // ------------------------------------------------------------------ the controller (VEX's picture, Steam style)

        // The picture (Resources/Controller/V5 Controller.png: VEX's catalog render of the V5 Controller, 276-4820, cut
        // out) and where its controls are in it (pixels of the 1200 x 864 image, from the top left).
        private const float PictureWidth = 430f, PictureAspect = 864f / 1200f, PictureY = 6f;
        private static readonly Dictionary<string, Vector2> Spots = new() {
            ["L1"] = new(278f, 26f), ["L2"] = new(312f, 40f), ["R1"] = new(922f, 26f), ["R2"] = new(888f, 40f),
            ["LStick"] = new(215f, 253f), ["RStick"] = new(985f, 253f),
            ["Up"] = new(359f, 341f), ["Left"] = new(285f, 416f), ["Right"] = new(433f, 416f), ["Down"] = new(359f, 489f),
            ["X"] = new(841f, 341f), ["Y"] = new(766f, 416f), ["A"] = new(913f, 416f), ["B"] = new(841f, 489f),
        };

        // Each side's rows, top to bottom, in the order of what they point at (few lines cross).
        private static readonly string[] LeftRows = { "L1", "L2", "Axis3+", "Axis4-", "Axis4+", "Axis3-", "Up", "Left", "Right", "Down" };
        private static readonly string[] RightRows = { "R1", "R2", "Axis2+", "Axis1-", "Axis1+", "Axis2-", "X", "Y", "A", "B" };
        private const float RowWidth = 178f, RowHeight = 32f, RowGap = 6f, ColumnX = 316f;

        private ShapeGraphic lines;
        private readonly Dictionary<string, (Vector2 from, Vector2 to)> callouts = new();
        private readonly Dictionary<string, TextMeshProUGUI> padLabels = new();

        private static Vector2 ToStage(Vector2 pixel) =>
            new((pixel.x / 1200f - 0.5f) * PictureWidth, PictureY + (0.5f - pixel.y / 864f) * PictureWidth * PictureAspect);

        // Where a control's line ends on the picture (a stick's directions just off its middle).
        private static Vector2 Target(string id) {
            const float off = 15f;
            return id switch {
                "Axis3+" => ToStage(Spots["LStick"]) + new Vector2(0f, off), "Axis3-" => ToStage(Spots["LStick"]) + new Vector2(0f, -off),
                "Axis4+" => ToStage(Spots["LStick"]) + new Vector2(off, 0f), "Axis4-" => ToStage(Spots["LStick"]) + new Vector2(-off, 0f),
                "Axis2+" => ToStage(Spots["RStick"]) + new Vector2(0f, off), "Axis2-" => ToStage(Spots["RStick"]) + new Vector2(0f, -off),
                "Axis1+" => ToStage(Spots["RStick"]) + new Vector2(off, 0f), "Axis1-" => ToStage(Spots["RStick"]) + new Vector2(-off, 0f),
                _ => ToStage(Spots[id]),
            };
        }

        private void DrawController() {
            var texture = Resources.Load<Texture2D>("Controller/V5 Controller");
            var picture = Ui.Rect("Picture", pad).gameObject.AddComponent<RawImage>();
            picture.texture = texture;
            picture.raycastTarget = false;
            picture.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, PictureY), new Vector2(PictureWidth, PictureWidth * PictureAspect));
            lines = ShapeGraphic.Create(pad);
            // Where the sticks are pushed: a dot over each.
            leftDot = Dot(ToStage(Spots["LStick"]));
            rightDot = Dot(ToStage(Spots["RStick"]));
            Column(LeftRows, -1f);
            Column(RightRows, 1f);
            var note = Ui.Text(pad, "Credit", "Picture: VEX Robotics (V5 Controller, 276-4820)", 10f, new Color(1f, 1f, 1f, 0.3f), null, TextAlignmentOptions.Center);
            note.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(400f, 16f));
        }

        private RectTransform Dot(Vector2 at) {
            var dot = Ui.Image(pad, "Stick Dot", new Color(0.157f, 0.522f, 0.957f, 0f));
            dot.sprite = Theme.Circle;
            dot.raycastTarget = false;
            dot.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), at, new Vector2(14f, 14f));
            return dot.rectTransform;
        }

        private void Column(string[] ids, float side) {
            var total = ids.Length * RowHeight + (ids.Length - 1) * RowGap;
            for (var i = 0; i < ids.Length; i++) {
                var y = total / 2f - RowHeight / 2f - i * (RowHeight + RowGap);
                var center = new Vector2(side * ColumnX, y);
                Row(ids[i], center);
                // The line leaves the row's inner side, runs level a little, then goes to the control.
                callouts[ids[i]] = (center - new Vector2(side * RowWidth / 2f, 0f), Target(ids[i]));
            }
        }

        private void Row(string id, Vector2 at) {
            var image = Ui.Image(pad, id, Control, 7f, true);
            image.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), at, new Vector2(RowWidth, RowHeight));
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
            var name = Ui.Text(image.transform, "Name", Short(id), 11.5f, new Color(1f, 1f, 1f, 0.6f), Theme.Medium);
            name.rectTransform.Fill(10, 0, RowWidth - 64f, 0);
            var key = Ui.Text(image.transform, "Key", "", 14f, Color.white, Theme.Bold, TextAlignmentOptions.MidlineLeft);
            key.rectTransform.Fill(62, 0, 58, 0);
            var padLabel = Ui.Text(image.transform, "Pad", "", 10.5f, new Color(1f, 1f, 1f, 0.45f), null, TextAlignmentOptions.MidlineRight);
            padLabel.rectTransform.Fill(RowWidth - 62f, 0, 8, 0);
            Ui.Tip(image, V5Controller.SlotName(id), null, "Click, then press a key or a gamepad control to bind it.");
            controls[id] = (image, key);
            padLabels[id] = padLabel;
        }

        private static string Short(string id) => id switch {
            "Axis3+" => "3 up", "Axis3-" => "3 down", "Axis4+" => "4 right", "Axis4-" => "4 left",
            "Axis2+" => "2 up", "Axis2-" => "2 down", "Axis1+" => "1 right", "Axis1-" => "1 left",
            _ => id,
        };

        private void DrawLines() {
            if (lines == null) return;
            lines.Clear();
            foreach (var (id, (from, to)) in callouts) {
                var held = V5Controller.Held(id);
                var chosen = id == selected;
                var color = held ? Lit : chosen ? new Color(1f, 1f, 1f, 0.85f) : new Color(1f, 1f, 1f, 0.3f);
                var width = chosen || held ? 2f : 1.2f;
                var bend = from + new Vector2(Mathf.Sign(to.x - from.x) * 18f, 0f);
                lines.Line(from, bend, color, width);
                lines.Line(bend, to, color, width);
                lines.Disc(to, chosen || held ? 4.5f : 3f, color);
            }
        }

        // ------------------------------------------------------------------ the chosen control

        private void Refresh() {
            if (this == null) return;
            var help = dialog.Body.Find("Presets/DriveHelp")?.GetComponent<TextMeshProUGUI>();
            if (help != null) help.text = Controls.DriveHelp(Controls.Drive).Replace(" (W/S)", "").Replace(" (Up/Down)", "").Replace(" (W/A/S/D)", "").Replace(" (Left/Right)", "");
            foreach (var (id, (_, key)) in controls) key.text = V5Controller.KeyLabel(id);
            foreach (var (id, label) in padLabels) label.text = V5Controller.PadLabel(id).Replace("L stick ", "").Replace("R stick ", "").Replace("D-pad ", "");
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

        private static void StickDot(RectTransform dot, Vector2 center, Vector2 push) {
            if (dot == null) return;
            dot.anchoredPosition = center + push * 20f;
            dot.GetComponent<Image>().color = new Color(0.157f, 0.522f, 0.957f, push.sqrMagnitude > 0.0001f ? 0.95f : 0f);
        }

        private void Update() {
            // V2: the connected light (a controller can be plugged in or out while this is open).
            var device = V5Controller.DeviceName;
            if (status != null && device != shownDevice) {
                shownDevice = device;
                status.text = device == null ? "No controller: keys only" : device + (V5Controller.Device is Gamepad ? "" : " (joystick)");
                status.color = device == null ? Theme.TextFaint : Theme.Text;
                statusDot.color = device == null ? Theme.TextFaint : Theme.Success;
                Ui.Tip(status, device == null ? "No controller" : device, null,
                    device == null ? "Plug in a gamepad (Xbox, PlayStation, Switch...) or a USB joystick and it works at once with these bindings."
                        : V5Controller.Device is Gamepad ? "Connected: the gamepad column shows what each control is on."
                        : "A plain joystick: its stick is the left stick, its second stick and hat the right stick and d-pad. Bind its buttons here: click a control, then press the button.");
            }
            // Held controls light up; the sticks show where they are.
            foreach (var (id, (image, _)) in controls)
                image.color = id == selected ? (V5Controller.Held(id) ? Lit : new Color(0.3f, 0.33f, 0.4f, 1f)) : V5Controller.Held(id) ? Lit : Control;
            StickDot(leftDot, ToStage(Spots["LStick"]), new Vector2(V5Controller.Axis(V5Axis.Axis4), V5Controller.Axis(V5Axis.Axis3)));
            StickDot(rightDot, ToStage(Spots["RStick"]), new Vector2(V5Controller.Axis(V5Axis.Axis1), V5Controller.Axis(V5Axis.Axis2)));
            DrawLines();

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
