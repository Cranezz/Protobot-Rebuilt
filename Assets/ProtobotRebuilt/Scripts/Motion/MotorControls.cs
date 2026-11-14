using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.Interface;
using Protobot.StateSystems;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Motion {
    // V2: click a motor (Select or Transform tool) and its controls pop up: its forward and reverse keys, its
    // gearbox (cartridge), its speed and direction. Only the motors being controlled run from their keys (hold
    // one): Ctrl+click more motors to control them together, set one key on all of them, and they run as one (two
    // motors on one geared axle). Motors geared into the same system that aren't controlled are outlined in red and
    // listed with Pair, which adds them. The window floats (drag its title; "-" folds it); while the mouse is on
    // it, each motor shows its name, keys and which way it turns.
    public class MotorControls : MonoBehaviour {
        private const string PositionKey = "V2.MotorControlsPosition", MinimizedKey = "V2.MotorControlsMinimized";
        private static MotorControls instance;
        private static readonly List<MotorDrive> controlled = new();

        private RectTransform window, header, body;
        private TextMeshProUGUI title, status;
        private Button minimizeButton;
        private bool minimized;
        private string shown;
        private int stateVersion, builtVersion = -1;
        private List<MotorDrive> geared = new();
        private DriveSystem[] systems = new DriveSystem[0];

        public static IReadOnlyList<MotorDrive> Controlled => controlled;
        public static bool IsControlled(MotorDrive motor) => controlled.Contains(motor);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init() {
            controlled.Clear();
            instance = null;
        }

        public static void Create(Transform root) {
            if (instance != null) return;
            instance = Ui.Rect("Motor Controls", root).Fill().gameObject.AddComponent<MotorControls>();
            instance.Build();
            StateSystem.OnChangeState += () => { if (instance != null) instance.stateVersion++; };
        }

        /// <summary>Controls these motors (the window shows them).</summary>
        public static void Control(IEnumerable<MotorDrive> motors, bool add = false) {
            if (!add) controlled.Clear();
            foreach (var m in motors)
                if (m != null && !controlled.Contains(m)) controlled.Add(m);
            if (instance != null) instance.SetMinimized(instance.minimized);
        }

        public static void Release() {
            foreach (var m in controlled) if (m != null) m.StopToggle();
            controlled.Clear();
        }

        // ------------------------------------------------------------------ window

        private void Build() {
            window = Ui.Rect("Window", transform);
            window.anchorMin = window.anchorMax = new Vector2(1f, 1f);
            window.pivot = new Vector2(1f, 1f);
            window.sizeDelta = new Vector2(380f, 40f);
            var parts = PlayerPrefs.GetString(PositionKey, "").Split(',');
            window.anchoredPosition = parts.Length == 2 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                                      && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                ? new Vector2(x, y) : new Vector2(-380f, -(Layout.TopBarBottom + 120f));  // left of the parts list

            header = Ui.Panel(window, "Header", Theme.PanelRaised, 10f).rectTransform;
            header.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(380f, 38f));
            header.GetChild(0).GetComponent<Image>().raycastTarget = true;
            header.gameObject.AddComponent<Mover>().owner = this;
            title = Ui.Text(header, "Title", "Motor", Theme.FontBody, Theme.Text, Theme.Medium);
            title.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(290f, 30f));
            title.overflowMode = TextOverflowModes.Ellipsis;
            var close = Ui.Button(header, "Close", "×", () => {
                Release();
                Interface.Interface.Say("Not controlling motors", true);
            }, false, 5f, Theme.FontTitle);
            ((RectTransform)close.transform).Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-6f, 0f), new Vector2(28f, 26f));
            Ui.Tip(close, "Close", null, "Stops controlling these motors (they stop).");
            minimizeButton = Ui.Button(header, "Minimize", "-", () => SetMinimized(!minimized), false, 5f, Theme.FontTitle);
            ((RectTransform)minimizeButton.transform).Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-38f, 0f), new Vector2(28f, 26f));
            Ui.Tip(minimizeButton, "Minimize", null, "Folds the controls into this bar (the keys still run the motors); click the bar to open them again.");

            body = Ui.Panel(window, "Body", Theme.Panel, 10f).rectTransform;
            body.anchorMin = body.anchorMax = new Vector2(1f, 1f);
            body.pivot = new Vector2(1f, 1f);
            body.anchoredPosition = new Vector2(0f, -42f);
            body.sizeDelta = new Vector2(380f, 0f);
            Ui.Column(body, 4f, new RectOffset(14, 14, 6, 14));
            body.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            body.GetChild(0).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            minimized = PlayerPrefs.GetInt(MinimizedKey, 0) == 1;
            window.gameObject.SetActive(false);
            KeepOnScreen();
        }

        private void SetMinimized(bool value) {
            minimized = value;
            PlayerPrefs.SetInt(MinimizedKey, value ? 1 : 0);
            body.gameObject.SetActive(!value);
            minimizeButton.GetComponentInChildren<TextMeshProUGUI>().text = value ? "+" : "-";
        }

        // ------------------------------------------------------------------ which motors

        private SelectionSystem.SelectionManager selection;
        private bool hadSelection;

        private SelectionSystem.SelectionManager Selection {
            get {
                if (selection == null)
                    selection = FindObjectsByType<SelectionSystem.ClickSelector>(FindObjectsSortMode.None)
                                .Select(c => c.GetComponent<SelectionSystem.SelectionManager>()).FirstOrDefault(m => m != null);
                return selection;
            }
        }

        // A click on a motor (in the Select or Transform tool) controls it; Ctrl+click adds or removes one; a click
        // elsewhere lets go.
        private void WatchClicks() {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame || MouseInput.overUI || Sandbox.Running) return;
            var tool = ToolRail.Current;
            if (tool != "Select" && tool != "Transform") return;
            if (Placement.IsPlacing || InputEvents.TextInput.ShortcutsPaused) return;
            // V2: not while a tool with its own clicks has the mouse (or the click that just finished it: a cable or
            // tube plugged into a motor's port was also taking control of the motor).
            if (SelectionSystem.SelectionManager.Blocked != null && SelectionSystem.SelectionManager.Blocked()) return;
            var ctrl = Keyboard.current != null && (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed);
            MotorDrive motor = null;
            var camera = Camera.main;
            if (camera != null && Physics.Raycast(camera.ScreenPointToRay(MouseInput.Position), out var hit, Mathf.Infinity, LayerMask.GetMask("Default", "HoleCollisions"))) {
                var target = SelectAs.Resolve(hit.collider.gameObject);
                var saved = target != null ? target.GetComponentInParent<SavedObject>() : null;
                if (saved != null) motor = saved.GetComponent<MotorDrive>();
            }
            if (motor != null) {
                hadSelection = false;  // the click selects it in a moment
                if (ctrl && controlled.Contains(motor)) {
                    controlled.Remove(motor);
                    motor.StopToggle();
                }
                else if (ctrl) controlled.Add(motor);
                else if (!(controlled.Count == 1 && controlled[0] == motor)) {
                    Release();
                    controlled.Add(motor);
                }
            }
            else if (!ctrl && controlled.Count > 0) Release();
        }

        // ------------------------------------------------------------------ contents

        // Not the speed: the slider being dragged would be made again.
        private string Signature() => string.Join("|", controlled.Select(m => m.GetInstanceID() + ":" + m.Rpm + ":" + Controls.ForwardKey(m.gameObject) + ":"
                                                                         + Controls.ReverseKey(m.gameObject) + ":" + Controls.Reversed(m.gameObject) + ":" + m.Label))
                                      + "#" + string.Join(",", MotorDrive.Live().Select(m => m.Label)) + "#" + stateVersion;

        // V2: laid out like the app's other windows: small headed sections (Keys, Gearbox, Speed, Motors, Geared
        // system), the keys as two cards side by side, the gearbox as three buttons, the system's state in a box.
        private void Rebuild() {
            for (var i = body.childCount - 1; i >= 1; i--) Destroy(body.GetChild(i).gameObject);
            var motors = controlled.ToList();
            var first = motors[0].gameObject;
            title.text = motors.Count == 1 ? motors[0].Label + "  ·  " + motors[0].Kind + " · " + motors[0].Rpm + " rpm" : motors.Count + " motors, controlled together";

            // Keys: Forward and Reverse cards.
            Section("Keys");
            var keys = Ui.Rect("Keys", body);
            var keysLayout = Ui.Row(keys, 8f);
            keysLayout.childForceExpandWidth = true;
            keys.Size(-1, 62f);
            KeyCard(keys, true, motors);
            KeyCard(keys, false, motors);
            status = Ui.Text(body, "Status", "", Theme.FontSmall, Theme.TextDim);
            status.Size(-1, 18f);

            // Gearbox: one button per cartridge.
            Section("Gearbox");
            var eleven = motors.Where(m => m.HasCartridge).ToList();
            if (eleven.Count > 0) {
                var gearbox = Row("Gearbox");
                gearbox.Size(-1, 34f);
                var chosen = eleven.Select(m => m.Rpm).Distinct().Count() == 1 ? System.Array.IndexOf(MotorDrive.Cartridges, eleven[0].Rpm) : -1;
                var labels = MotorDrive.Cartridges.Select(r => char.ToUpper(MotorDrive.CartridgeName(r)[0]) + MotorDrive.CartridgeName(r).Substring(1) + "  " + r).ToArray();
                ToolOptions.Segmented(gearbox, labels, chosen, i => {
                    foreach (var m in eleven) m.SetCartridge(MotorDrive.Cartridges[i], false);
                    Interface.Interface.Say(Names(eleven) + ": " + MotorDrive.CartridgeName(MotorDrive.Cartridges[i]) + " cartridge, " + MotorDrive.Cartridges[i] + " rpm", true);
                    Audio.Sounds.Play(Audio.Sounds.Tick);
                    shown = null;
                });
                Ui.Tip(gearbox.GetComponentInChildren<Button>(), "Gearbox cartridge", null, "Red 36:1, 100 rpm, 2.1 N·m (arms, lifts) · Green 18:1, 200 rpm, 1.05 N·m (drives) · Blue 6:1, 600 rpm, 0.35 N·m (flywheels, intakes).");
                var note = chosen < 0 ? "Different cartridges: pick one for all of them." : "Red: arms, lifts · Green: drives · Blue: flywheels";
                if (eleven.Count < motors.Count) note = "The 5.5W motors keep 200 rpm.";
                Ui.Text(body, "Gearbox Note", note, Theme.FontSmall - 1f, Theme.TextFaint).Size(-1, 18f);
            }
            else Ui.Text(body, "Gearbox Note", "The 5.5W motor has no cartridge: 200 rpm, 0.5 N·m.", Theme.FontSmall, Theme.TextDim).Size(-1, 22f);

            // Speed.
            Section("Speed");
            var speedRow = Row("Speed");
            var share = Controls.Speed(first);
            var slider = MotionUi.Slider(speedRow, share, 0.05f, 0.05f, v => Mathf.RoundToInt(v * 100f) + "%  ·  " + Mathf.RoundToInt(v * motors[0].Rpm) + " rpm",
                v => { foreach (var m in motors) Controls.SetSpeed(m.gameObject, v); },
                v => Interface.Interface.Say(Names(motors) + ": " + Mathf.RoundToInt(v * 100f) + "% speed", true));
            slider.Size(-1, 28f, 1f);

            // Each controlled motor: name, kind, reversed.
            Section(motors.Count == 1 ? "Motor" : "Motors");
            foreach (var motor in motors) MotorRow(motor, motors.Count > 1);

            // The systems these motors drive, and the motors in them not controlled.
            var rotors = GearTrain.Current();
            var found = new List<DriveSystem>();
            foreach (var m in motors) {
                if (found.Any(s => s.Motors.Contains(m))) continue;
                var system = GearTrain.SystemOf(m, rotors);
                if (system != null) found.Add(system);
            }
            systems = found.ToArray();
            geared = systems.SelectMany(s => s.Motors).Distinct().Where(m => !controlled.Contains(m)).ToList();
            Section("Geared system");
            var shafts = systems.SelectMany(s => s.rotors).Distinct().Count();
            var gears = systems.SelectMany(s => s.meshes).Select(m => m.a.GetHashCode() < m.b.GetHashCode() ? (m.a, m.b) : (m.b, m.a)).Distinct().Count();
            var summary = systems.Length == 0 ? "Nothing to turn yet: put a shaft in " + (motors.Count == 1 ? "its" : "their") + " square hole."
                : shafts + (shafts == 1 ? " shaft turns" : " shafts turn") + (gears > 0 ? ", " + gears + (gears == 1 ? " gear mesh" : " gear meshes") : "") + ".";
            Callout(summary, Theme.TextDim);
            // Controlled motors in one system that would turn it opposite ways fight each other.
            foreach (var system in systems.Distinct()) {
                var mine = motors.Where(m => system.Motors.Contains(m)).ToList();
                if (mine.Count < 2) continue;
                float Way(MotorDrive m) => Mathf.Sign(system.MotorRatio(m)) * (Controls.Reversed(m.gameObject) ? -1f : 1f);
                var against = mine.Where(m => Way(m) != Way(mine[0])).ToList();
                if (against.Count == 0) continue;
                Callout(Names(against) + " would turn the gears the other way from " + mine[0].Label + ": reverse " + (against.Count == 1 ? "it" : "them") + " so they work together.", Theme.Warning);
            }
            foreach (var jam in systems.Where(s => s.jammed).Select(s => s.jamReason).Distinct())
                Callout("Can't turn: " + jam, Theme.Danger);
            if (geared.Count > 0) {
                Ui.Text(body, "Geared", "Also in this system (outlined red), turned along:", Theme.FontSmall, Theme.TextDim).Size(-1, 20f);
                foreach (var other in geared) {
                    var row = Row(other.Label);
                    var dot = Ui.Image(row, "Cartridge", other.Color, 5f);
                    dot.Size(10f, 10f);
                    var name = Ui.Text(row, "Name", other.Label, Theme.FontBody, Theme.Text);
                    name.Size(-1, 28f, 1f);
                    name.overflowMode = TextOverflowModes.Ellipsis;
                    Ui.Text(row, "Kind", other.Kind + " · " + other.Rpm, Theme.FontSmall, Theme.TextFaint).Size(74f, 28f);
                    var target = other;
                    var pair = Ui.Button(row, "Pair", "Pair", () => {
                        Control(new[] { target }, true);
                        Interface.Interface.Say(target.Label + " paired: it's controlled with " + Names(controlled.Where(m => m != target)), true);
                        Audio.Sounds.Play(Audio.Sounds.Tick);
                    }, true, 5f, Theme.FontSmall);
                    pair.Size(58f, 26f);
                    Ui.Tip(pair, "Pair", null, "Controls " + other.Label + " with these (it gets their keys, speed and gearbox).");
                }
            }
        }

        private void Section(string text) {
            var heading = Dialog.Heading(body, text);
            heading.margin = new Vector4(0, 6, 0, 0);
            heading.Size(-1, 24f);
        }

        private void Callout(string text, Color color) {
            var box = Ui.Image(body, "Callout", Theme.PanelRaised, 7f);
            Ui.Column(box.rectTransform, 0f, new RectOffset(10, 10, 6, 6));
            var label = Ui.Text(box.transform, "Text", text, Theme.FontSmall, color);
            label.textWrappingMode = TextWrappingModes.Normal;
            var lines = Mathf.Max(1, Mathf.CeilToInt(text.Length / 46f));
            label.Size(-1, 16f * lines + 2f);
            box.Size(-1, 16f * lines + 14f);
        }

        // A key card: its label and the key (click it, press a key).
        private void KeyCard(RectTransform parent, bool forward, List<MotorDrive> motors) {
            var card = Ui.Image(parent, forward ? "Forward" : "Reverse", Theme.PanelRaised, 8f);
            var column = Ui.Column(card.rectTransform, 4f, new RectOffset(10, 10, 6, 8));
            column.childForceExpandWidth = true;
            card.Size(-1, 62f, 1f);
            Ui.Text(card.transform, "Label", forward ? "Forward" : "Reverse", Theme.FontSmall, Theme.TextDim).Size(-1, 18f);
            var keyOf = forward ? (System.Func<GameObject, Key>)Controls.ForwardKey : Controls.ReverseKey;
            var mixed = motors.Select(m => keyOf(m.gameObject)).Distinct().Count() > 1;
            var chip = MotionUi.KeyChip(card.transform, keyOf(motors[0].gameObject), key => SetKey(key, forward),
                forward ? "Forward key" : "Reverse key", "Hold it to run " + Names(motors) + (forward ? " forward." : " in reverse."), mixed);
            chip.Size(-1, 28f);
        }

        private void MotorRow(MotorDrive motor, bool canUnpair) {
            var row = Row(motor.Label);
            row.Size(-1, 32f);
            var dot = Ui.Image(row, "Cartridge", motor.Color, 5f);
            dot.Size(10f, 10f);
            Ui.Tip(dot, "Cartridge color", null, motor.Kind == "11W" ? MotorDrive.CartridgeName(motor.Rpm) + ", " + motor.Rpm + " rpm" : "5.5W motor, 200 rpm");
            var field = Ui.Field(row, "Name", Controls.GivenName(motor.gameObject) ?? "", value => {
                var before = motor.Label;
                Controls.SetName(motor.gameObject, value);
                if (before != motor.Label) Interface.Interface.Say(before + " is now " + motor.Label, true);
            });
            field.Size(-1, 28f, 1f);
            if (string.IsNullOrEmpty(field.text) && field.placeholder == null) {
                var hint = Ui.Text(field.transform.Find("Text Area"), "Placeholder", "Motor " + motor.Number, Theme.FontBody, Theme.TextFaint);
                hint.rectTransform.Fill();
                field.placeholder = hint;
            }
            Ui.Tip(field, "Name", null, "A name for this motor (\"Left drive\", \"Arm\"); empty: Motor " + motor.Number + ".");
            var reversed = Controls.Reversed(motor.gameObject);
            var rev = Ui.Button(row, "Reversed", reversed ? "Reversed" : "Normal", () => {
                Controls.SetReversed(motor.gameObject, !Controls.Reversed(motor.gameObject));
                Interface.Interface.Say(motor.Label + (Controls.Reversed(motor.gameObject) ? " reversed: forward turns the other way" : " turns the normal way"), true);
                Audio.Sounds.Play(Audio.Sounds.Tick);
                shown = null;
            }, reversed, 5f, Theme.FontSmall);
            rev.Size(84f, 28f);
            if (!reversed) {
                rev.targetGraphic.color = Theme.Field;
                rev.GetComponentInChildren<TextMeshProUGUI>().color = Theme.TextDim;
            }
            Ui.Tip(rev, "Direction", null, "Reversed: forward turns this motor the other way (two motors facing each other on one system, or the right side of a drive).");
            if (canUnpair) {
                var off = Ui.Button(row, "Unpair", "×", () => {
                    controlled.Remove(motor);
                    motor.StopToggle();
                    Interface.Interface.Say(motor.Label + " isn't controlled with the others now", true);
                }, false, 5f, Theme.FontBody);
                off.Size(28f, 28f);
                Ui.Tip(off, "Unpair", null, "Stops controlling " + motor.Label + " with these.");
            }
        }

        private void SetKey(Key key, bool forward) {
            var shortcut = key == Key.None ? null : Controls.ShortcutOn(key);
            if (shortcut != null) {
                Interface.Interface.Say(Controls.KeyName(key) + " is the shortcut for " + shortcut + ": pick another key", false);
                Audio.Sounds.Play(Audio.Sounds.Error);
                shown = null;
                return;
            }
            foreach (var m in controlled) {
                if (forward) Controls.SetForwardKey(m.gameObject, key);
                else Controls.SetReverseKey(m.gameObject, key);
            }
            Interface.Interface.Say(Names(controlled) + (forward ? " forward: " : " reverse: ") + (key == Key.None ? "no key" : "hold " + Controls.KeyName(key)), true);
            Audio.Sounds.Play(Audio.Sounds.Tick);
        }

        private static string Names(IEnumerable<MotorDrive> motors) {
            var list = motors.Where(m => m != null).Select(m => m.Label).ToList();
            return list.Count <= 2 ? string.Join(" and ", list) : string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1];
        }

        private RectTransform Row(string name) {
            var row = Ui.Rect(name, body);
            Ui.Row(row, 8f);
            row.Size(-1, 30f);
            return row;
        }

        private static void Label(RectTransform row, string text, float width, bool dim = false) {
            var label = Ui.Text(row, "Label", text, Theme.FontSmall, dim ? Theme.TextFaint : Theme.TextDim);
            label.Size(width, 28f);
        }

        // ------------------------------------------------------------------ every frame

        private void Update() {
            WatchClicks();
            controlled.RemoveAll(m => m == null || m.gameObject.IsDeleted());
            // The selection let go of (Esc, a click on nothing): the motors too.
            if (Selection != null) {
                if (Selection.current != null) hadSelection = true;
                else if (hadSelection) {
                    hadSelection = false;
                    if (controlled.Count > 0 && !MotionUi.Capturing && !Sandbox.Running) Release();
                }
            }
            var show = controlled.Count > 0 && !Sandbox.Running;
            if (window.gameObject.activeSelf != show) {
                window.gameObject.SetActive(show);
                if (show) SetMinimized(minimized);
            }
            if (!show) {
                Highlights.Clear("unpaired");
                shown = null;
                return;
            }
            var signature = Signature();
            if (signature != shown) {
                shown = signature;
                if (!minimized || builtVersion != stateVersion) Rebuild();
                builtVersion = stateVersion;
            }
            Highlights.Set("unpaired", geared.Where(m => m != null).Select(m => m.gameObject), Highlights.Unpaired, true);

            if (status != null) {
                var running = controlled.FirstOrDefault(m => Mathf.Abs(m.Level) > 0.01f);
                var k1 = Controls.KeyName(Controls.ForwardKey(controlled[0].gameObject));
                var k2 = Controls.KeyName(Controls.ReverseKey(controlled[0].gameObject));
                status.text = running != null
                    ? (running.Level > 0 ? "Running forward · " : "Running in reverse · ") + Mathf.RoundToInt(Mathf.Abs(running.Level) * running.Rpm) + " rpm"
                    : "Hold " + k1 + " to run forward, " + k2 + " in reverse";
            }

            // Names over the motors while the mouse is on the window.
            var mouse = MouseInput.Position;
            if (RectTransformUtility.RectangleContainsScreenPoint(header, mouse, null)
                || (!minimized && RectTransformUtility.RectangleContainsScreenPoint(body, mouse, null)))
                MotionUi.DrawLabels(controlled.Concat(geared), new PneumaticCylinder[0]);
        }

        private void KeepOnScreen() {
            if (window == null || Interface.Interface.Root == null) return;
            var root = Interface.Interface.Root.rect;
            var p = window.anchoredPosition;
            p.x = Mathf.Clamp(p.x, -root.width + 120f, 0f);
            p.y = Mathf.Clamp(p.y, -root.height + 34f, 0f);
            window.anchoredPosition = p;
        }

        private class Mover : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler {
            public MotorControls owner;
            private bool moved;

            public void OnPointerDown(PointerEventData e) => moved = false;

            public void OnDrag(PointerEventData e) {
                moved = true;
                owner.window.anchoredPosition += e.delta / Mathf.Max(Interface.Interface.Root.lossyScale.x, 0.0001f);
                owner.KeepOnScreen();
            }

            public void OnPointerUp(PointerEventData e) {
                var p = owner.window.anchoredPosition;
                PlayerPrefs.SetString(PositionKey, p.x.ToString(CultureInfo.InvariantCulture) + "," + p.y.ToString(CultureInfo.InvariantCulture));
                if (!moved && owner.minimized) owner.SetMinimized(false);
            }
        }
    }
}
