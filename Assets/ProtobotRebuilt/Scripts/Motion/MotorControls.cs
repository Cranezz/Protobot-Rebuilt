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
            window.sizeDelta = new Vector2(360f, 40f);
            var parts = PlayerPrefs.GetString(PositionKey, "").Split(',');
            window.anchoredPosition = parts.Length == 2 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                                      && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                ? new Vector2(x, y) : new Vector2(-380f, -(Layout.TopBarBottom + 120f));  // left of the parts list

            header = Ui.Panel(window, "Header", Theme.PanelRaised, 10f).rectTransform;
            header.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(360f, 34f));
            header.GetChild(0).GetComponent<Image>().raycastTarget = true;
            header.gameObject.AddComponent<Mover>().owner = this;
            title = Ui.Text(header, "Title", "Motor", Theme.FontBody, Theme.Text, Theme.Medium);
            title.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(270f, 30f));
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
            body.anchoredPosition = new Vector2(0f, -38f);
            body.sizeDelta = new Vector2(360f, 0f);
            Ui.Column(body, 6f, new RectOffset(12, 12, 10, 12));
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
                                                                         + Controls.ReverseKey(m.gameObject) + ":" + Controls.Reversed(m.gameObject)))
                                      + "#" + string.Join(",", MotorDrive.Live().Select(m => m.Label)) + "#" + stateVersion;

        private void Rebuild() {
            for (var i = body.childCount - 1; i >= 1; i--) Destroy(body.GetChild(i).gameObject);
            var motors = controlled.ToList();
            var first = motors[0].gameObject;
            title.text = motors.Count == 1 ? motors[0].Label + " · " + motors[0].Kind : string.Join(", ", motors.Select(m => m.Label));

            // Keys.
            var keys = Row("Keys");
            Label(keys, "Forward", 58f);
            var forwardMixed = motors.Select(m => Controls.ForwardKey(m.gameObject)).Distinct().Count() > 1;
            MotionUi.KeyChip(keys, Controls.ForwardKey(first), key => SetKey(key, true), "Forward key", "Hold it to run " + Names(motors) + " forward.", forwardMixed);
            Label(keys, "Reverse", 58f);
            var reverseMixed = motors.Select(m => Controls.ReverseKey(m.gameObject)).Distinct().Count() > 1;
            MotionUi.KeyChip(keys, Controls.ReverseKey(first), key => SetKey(key, false), "Reverse key", "Hold it to run " + Names(motors) + " in reverse.", reverseMixed);

            // Gearbox.
            var eleven = motors.Where(m => m.HasCartridge).ToList();
            var gearbox = Row("Gearbox");
            Label(gearbox, "Gearbox", 58f);
            if (eleven.Count > 0) {
                var labels = MotorDrive.Cartridges.Select(r => char.ToUpper(MotorDrive.CartridgeName(r)[0]) + MotorDrive.CartridgeName(r).Substring(1) + " · " + r + " rpm").ToArray();
                var chosen = eleven.Select(m => m.Rpm).Distinct().Count() == 1 ? System.Array.IndexOf(MotorDrive.Cartridges, eleven[0].Rpm) : -1;
                var choice = Popup.Choice(gearbox, labels, chosen, i => {
                    foreach (var m in eleven) m.SetCartridge(MotorDrive.Cartridges[i], false);
                    Interface.Interface.Say(Names(eleven) + ": " + MotorDrive.CartridgeName(MotorDrive.Cartridges[i]) + " cartridge, " + MotorDrive.Cartridges[i] + " rpm", true);
                    Audio.Sounds.Play(Audio.Sounds.Tick);
                }, 200f);
                if (chosen < 0) choice.GetComponentInChildren<TextMeshProUGUI>().text = "mixed";
                Ui.Tip(choice, "Gearbox cartridge", null, "Red 36:1, 100 rpm, 2.1 N·m (arms, lifts) · Green 18:1, 200 rpm, 1.05 N·m (drives) · Blue 6:1, 600 rpm, 0.35 N·m (flywheels, intakes).");
                if (eleven.Count < motors.Count) Label(gearbox, "5.5W: 200 rpm", 90f, true);
            }
            else Label(gearbox, "None: the 5.5W motor turns at 200 rpm (0.5 N·m)", 260f, true);

            // Speed.
            var speedRow = Row("Speed");
            Label(speedRow, "Speed", 58f);
            var share = Controls.Speed(first);
            var slider = MotionUi.Slider(speedRow, share, 0.05f, 0.05f, v => Mathf.RoundToInt(v * 100f) + "% · " + Mathf.RoundToInt(v * motors[0].Rpm) + " rpm",
                v => { foreach (var m in motors) Controls.SetSpeed(m.gameObject, v); },
                v => Interface.Interface.Say(Names(motors) + ": " + Mathf.RoundToInt(v * 100f) + "% speed", true));
            slider.Size(-1, 26f, 1f);

            // Direction, name.
            var reversed = motors.All(m => Controls.Reversed(m.gameObject));
            ToolOptions.Switch(body, "Reversed (forward turns the other way)", reversed, on => {
                foreach (var m in motors) Controls.SetReversed(m.gameObject, on);
                Interface.Interface.Say(Names(motors) + (on ? " reversed" : " not reversed"), true);
            });
            if (motors.Count == 1) {
                var nameRow = Row("Name");
                Label(nameRow, "Name", 58f);
                var motor = motors[0];
                var field = Ui.Field(nameRow, "Name", Controls.GivenName(motor.gameObject) ?? "", value => {
                    var before = motor.Label;
                    Controls.SetName(motor.gameObject, value);
                    Interface.Interface.Say(before == motor.Label ? motor.Label : before + " is now " + motor.Label, true);
                });
                field.Size(-1, 28f, 1f);
                Ui.Tip(field, "Name", null, "A name for this motor (\"Left drive\", \"Arm\"); empty: Motor " + motor.Number + ".");
            }

            status = Ui.Text(body, "Status", "", Theme.FontSmall, Theme.TextDim);
            status.Size(-1, 22f);

            // The systems these motors drive, and the motors in them not controlled.
            var rotors = GearTrain.Rotors();
            systems = motors.Select(m => GearTrain.SystemOf(m, rotors)).Where(s => s != null).ToArray();
            geared = systems.SelectMany(s => s.Motors).Distinct().Where(m => !controlled.Contains(m)).ToList();
            Ui.Divider(body);
            var shafts = systems.SelectMany(s => s.rotors).Distinct().Count();
            var gears = systems.SelectMany(s => s.meshes).Select(m => m.a.GetHashCode() < m.b.GetHashCode() ? (m.a, m.b) : (m.b, m.a)).Distinct().Count();
            var summary = systems.Length == 0 ? "No shaft in " + (motors.Count == 1 ? "its" : "their") + " square hole yet: put one in to turn things"
                : shafts + (shafts == 1 ? " shaft turns" : " geared shafts turn") + (gears > 0 ? " (" + gears + (gears == 1 ? " gear mesh)" : " gear meshes)") : "");
            var info = Ui.Text(body, "System", summary, Theme.FontSmall, Theme.TextDim);
            info.textWrappingMode = TextWrappingModes.Normal;
            info.Size(-1, 20f);
            // Controlled motors in one system that would turn it opposite ways fight each other.
            foreach (var system in systems.Distinct()) {
                var mine = motors.Where(m => system.Motors.Contains(m)).ToList();
                if (mine.Count < 2) continue;
                float Way(MotorDrive m) => Mathf.Sign(system.MotorRatio(m)) * (Controls.Reversed(m.gameObject) ? -1f : 1f);
                var against = mine.Where(m => Way(m) != Way(mine[0])).ToList();
                if (against.Count == 0) continue;
                var warn = Ui.Text(body, "Against", Names(against) + " would turn the gears the other way from " + mine[0].Label + ": reverse " + (against.Count == 1 ? "it" : "them") + " so they work together.", Theme.FontSmall, Theme.Warning);
                warn.textWrappingMode = TextWrappingModes.Normal;
                warn.Size(-1, 34f);
            }
            foreach (var jam in systems.Where(s => s.jammed).Select(s => s.jamReason).Distinct()) {
                var warn = Ui.Text(body, "Jammed", "Can't turn: " + jam, Theme.FontSmall, Theme.Danger);
                warn.textWrappingMode = TextWrappingModes.Normal;
                warn.Size(-1, 34f);
            }
            if (geared.Count > 0) {
                var heading = Ui.Text(body, "Geared", "Also in this system (red), turned along but not controlled:", Theme.FontSmall, Theme.Text);
                heading.textWrappingMode = TextWrappingModes.Normal;
                heading.Size(-1, 34f);
                foreach (var other in geared) {
                    var row = Row(other.Label);
                    var dot = Ui.Image(row, "Cartridge", other.Color, 5f);
                    dot.Size(10f, 10f);
                    var name = Ui.Text(row, "Name", other.Label + " · " + other.Kind + " · " + other.Rpm + " rpm", Theme.FontBody, Theme.Text);
                    name.Size(-1, 28f, 1f);
                    var target = other;
                    var pair = Ui.Button(row, "Pair", "Pair", () => {
                        Control(new[] { target }, true);
                        Interface.Interface.Say(target.Label + " paired: it's controlled with " + Names(controlled.Where(m => m != target)), true);
                        Audio.Sounds.Play(Audio.Sounds.Tick);
                    }, true, 5f, Theme.FontSmall);
                    pair.Size(-1, 26f);
                    Ui.Tip(pair, "Pair", null, "Controls " + other.Label + " with these (it gets their keys, speed and gearbox; set them to match).");
                }
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
