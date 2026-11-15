using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.Interface;
using Protobot.Motion;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Devices {
    // V2: the robot's devices as the V5 Brain shows them, in the app's look. Along the top, like the Brain's Device
    // Info screen: Devices (the 21 smart ports, then a card per device), 3-Wire (ports A-H with each one's mode and
    // value), Controller (the drive) and the battery. A motor's card shows its port, cartridge color and speed, with
    // < ■ > to run it by hand as on the Brain; opened, it's the Brain's motor dashboard (gears, revolutions, degrees,
    // velocity, power and torque against speed) and its settings (name, keys or drive side, reversed). Sensors show
    // what the real ones read, live; solenoids their port, mode (Digital Out), value and the cylinders' position, with
    // a switch. While the simulation runs it also has Pause, Reset and Stop. Replaces the old motor and Controls windows.
    public partial class DevicesPanel : MonoBehaviour {
        private const string PositionKey = "V2.DevicesPanelPosition", MinimizedKey = "V2.DevicesPanelMinimized", SizeKey = "V2.DevicesPanelSize";
        private const float MinWidth = 380f, MinHeight = 300f, ColumnWidth = 310f;
        // V2: the window's size (drag its edges): cards flow into as many columns as fit.
        private float width = 720f, heightMax = 760f;
        private int Columns => Mathf.Clamp(Mathf.FloorToInt((width - 30f) / ColumnWidth), 1, 4);
        private static DevicesPanel open;

        private enum Page { Devices, ThreeWire, Controller }

        private RectTransform window, frame, content, tabs, minimizedHide;
        private TextMeshProUGUI subtitle, status;
        private ScrollRect scroll;
        private LayoutElement frameLayout;
        private Page page;
        private bool minimized, right, bySimulation, simulation;
        private readonly HashSet<string> expanded = new();
        private string focusKey, shown;
        private readonly List<Action> live = new();
        private readonly List<(RectTransform rect, V5Device device)> cards = new();

        public static bool IsOpen => open != null;
        public static bool MouseOver => open != null && open.window != null && open.window.gameObject.activeInHierarchy
                                        && RectTransformUtility.RectangleContainsScreenPoint(open.window, MouseInput.Position, null);

        /// <summary>Opens it; `right`: the game's setup (beside its controls card, top right).</summary>
        public static void Open() => Open(false);

        public static void Open(bool right) {
            if (open != null) {
                if (open.simulation != Sandbox.Running) open.shown = null;
                return;
            }
            if (Interface.Interface.Root == null) return;
            var go = Ui.Rect("Devices Panel", Interface.Interface.Root).Fill().gameObject;
            open = go.AddComponent<DevicesPanel>();
            open.right = right;
            open.bySimulation = Sandbox.Running;
            open.Build();
        }

        public static void Close() {
            if (open == null) return;
            foreach (var m in MotorDrive.Live()) m.Manual = 0f;   // leaving the motor screen stops it, as on the Brain
            Highlights.Clear("devices");
            Destroy(open.gameObject);
            open = null;
        }

        public static void Toggle() {
            if (open != null) Close();
            else Open();
        }

        public static void SimulationEnded() {
            if (open == null) return;
            if (open.bySimulation) Close();
            else open.shown = null;
        }

        /// <summary>Opens it on a device's card (opened, scrolled to).</summary>
        public static void Focus(GameObject part, bool quiet = false) {
            Open();
            if (open == null) return;
            var device = V5Brain.Of(part);
            if (device == null) return;
            open.page = Page.Devices;
            open.expanded.Add(device.Key);
            if (!quiet) open.focusKey = device.Key;
            open.SetMinimized(false);
            open.shown = null;
        }

        // ------------------------------------------------------------------ the window

        private void Build() {
            window = Ui.Panel(transform, "Window", Theme.Panel, 10f).rectTransform;
            window.anchorMin = window.anchorMax = window.pivot = new Vector2(1f, 1f);
            var size = PlayerPrefs.GetString(SizeKey, "").Split(',');
            if (size.Length == 2 && float.TryParse(size[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var sw)
                && float.TryParse(size[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var sh)) {
                width = Mathf.Max(MinWidth, sw);
                heightMax = Mathf.Max(MinHeight, sh);
            }
            window.sizeDelta = new Vector2(width, 0f);
            var parts = PlayerPrefs.GetString(PositionKey, "").Split(',');
            window.anchoredPosition = right ? new Vector2(-330f, -(Layout.TopBarBottom + 12f))
                : parts.Length == 2 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                  && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ? new Vector2(x, y)
                : new Vector2(-450f, -(Layout.TopBarBottom + 12f));
            Ui.Column(window, 8f, new RectOffset(12, 12, 8, 10));
            window.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            window.GetChild(0).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            // Header: drag it to move the window.
            var header = Ui.Image(window, "Header", new Color(0f, 0f, 0f, 0f), 0f, true).rectTransform;
            Ui.Row(header, 6f);
            header.Size(-1, 32f);
            header.gameObject.AddComponent<Mover>().owner = this;
            var mark = Ui.Image(header, "Brain", Theme.Accent, 4f);
            mark.Size(16f, 16f);
            var title = Ui.Text(header, "Title", "V5 Brain", Theme.FontTitle, Theme.Text, Theme.Medium);
            title.Size(84f, 30f);
            subtitle = Ui.Text(header, "Subtitle", "", Theme.FontSmall, Theme.TextDim);
            subtitle.Size(-1, 30f, 1f);
            simulation = Sandbox.Running;
            if (simulation) {
                var pause = Ui.Button(header, "Pause", "Pause", null, false, 5f, Theme.FontSmall);
                pause.onClick.AddListener(() => {
                    if (Sandbox.Instance == null) return;
                    Sandbox.Instance.SetPaused(!Sandbox.Instance.Paused);
                    pause.GetComponentInChildren<TextMeshProUGUI>().text = Sandbox.Instance.Paused ? "Run" : "Pause";
                });
                pause.Size(-1, 26f);
                Ui.Tip(pause, "Pause", null, "Stops time (\".\" steps it while paused).");
                var reset = Ui.Button(header, "Reset", "Reset", Sandbox.Reset, false, 5f, Theme.FontSmall);
                reset.Size(-1, 26f);
                Ui.Tip(reset, "Reset", null, "Starts the simulation again from the build.");
                var stop = Ui.Button(header, "Stop", "Stop", Sandbox.Stop, true, 5f, Theme.FontSmall);
                stop.Size(-1, 26f);
                Ui.Tip(stop, "Stop simulating", "Esc", "Back to building; the build is as it was.");
            }
            var fold = Ui.Button(header, "Minimize", "–", () => SetMinimized(!minimized), false, 5f, Theme.FontTitle);
            fold.Size(28f, 26f);
            Ui.Tip(fold, "Minimize", null, "Folds the window into its title bar; click the bar to open it again.");
            if (!simulation) {
                var close = Ui.Button(header, "Close", "×", Close, false, 5f, Theme.FontTitle);
                close.Size(28f, 26f);
                Ui.Tip(close, "Close", null, "Closes the devices (motors run by hand stop).");
            }

            minimizedHide = Ui.Rect("Contents", window);
            Ui.Column(minimizedHide, 8f);
            minimizedHide.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            tabs = Ui.Rect("Tabs", minimizedHide);
            var tabsLayout = Ui.Row(tabs, 6f);
            tabsLayout.childForceExpandWidth = true;
            tabs.Size(-1, 50f);

            frame = Ui.Rect("Scroll", minimizedHide);
            frameLayout = frame.gameObject.AddComponent<LayoutElement>();
            frameLayout.preferredHeight = 200f;
            frame.gameObject.AddComponent<RectMask2D>();
            content = Ui.Rect("Content", frame);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            Ui.Column(content, 6f, new RectOffset(0, 6, 0, 4));
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll = frame.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = frame;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            status = Ui.Text(minimizedHide, "Status", "", Theme.FontSmall, Theme.TextDim);
            status.textWrappingMode = TextWrappingModes.Normal;
            status.Size(-1, 20f);

            minimized = PlayerPrefs.GetInt(MinimizedKey, 0) == 1 && !right;
            SetMinimized(minimized);
            // Edges and corners to resize it by.
            Grip("Left", new Vector2(0f, 0f), new Vector2(0f, 1f), -1, 0);
            Grip("Right", new Vector2(1f, 0f), new Vector2(1f, 1f), 1, 0);
            Grip("Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), 0, -1);
            Grip("Bottom Left", Vector2.zero, Vector2.zero, -1, -1);
            Grip("Bottom Right", new Vector2(1f, 0f), new Vector2(1f, 0f), 1, -1);
            KeepOnScreen();
        }

        private void SetMinimized(bool value) {
            minimized = value;
            PlayerPrefs.SetInt(MinimizedKey, value ? 1 : 0);
            if (minimizedHide != null) minimizedHide.gameObject.SetActive(!value);
        }

        private void KeepOnScreen() {
            if (window == null || Interface.Interface.Root == null) return;
            var root = ((RectTransform)Interface.Interface.Root).rect;
            var p = window.anchoredPosition;
            p.x = Mathf.Clamp(p.x, -root.width + width * 0.3f, 0f);
            p.y = Mathf.Clamp(p.y, -root.height + 40f, 0f);
            window.anchoredPosition = p;
        }

        private class Mover : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler {
            public DevicesPanel owner;
            private bool moved;

            public void OnPointerDown(PointerEventData e) => moved = false;

            public void OnDrag(PointerEventData e) {
                moved = true;
                owner.window.anchoredPosition += e.delta / Mathf.Max(Interface.Interface.Root.lossyScale.x, 0.0001f);
                owner.KeepOnScreen();
            }

            public void OnPointerUp(PointerEventData e) {
                if (!owner.right) {
                    var p = owner.window.anchoredPosition;
                    PlayerPrefs.SetString(PositionKey, p.x.ToString(CultureInfo.InvariantCulture) + "," + p.y.ToString(CultureInfo.InvariantCulture));
                }
                if (!moved && owner.minimized) owner.SetMinimized(false);
            }
        }

        // ------------------------------------------------------------------ every frame

        private string Signature() {
            var sb = new System.Text.StringBuilder();
            sb.Append(page).Append(Sandbox.Running).Append(Controls.Drive).Append(Columns).Append(Mathf.RoundToInt(width / 24f)).Append('|');
            foreach (var d in V5Brain.Devices) {
                sb.Append(d.Key).Append(d.port).Append(d.wired).Append(d.Name).Append(expanded.Contains(d.Key));
                if (d.part != null) sb.Append(Dynamics.Token(d.part, "cartridge")).Append(Dynamics.Token(d.part, "side")).Append(Dynamics.Token(d.part, "rev"))
                    .Append(Dynamics.Token(d.part, "fkey")).Append(Dynamics.Token(d.part, "rkey"));
                if (d is SolenoidDevice s)
                    foreach (var c in s.cylinders) if (c != null) sb.Append(c.Label).Append(c.Ports).Append(Controls.CylinderKey(c.gameObject));
                if (d is MotorDevice m) sb.Append(MotorControls.IsControlled(m.motor));
                sb.Append(';');
            }
            return sb.ToString();
        }

        private void Update() {
            if (Sandbox.Running != simulation) {   // the header's buttons change
                var keep = (page, expanded.ToList(), bySimulation);
                Destroy(gameObject);
                open = null;
                Open(right);
                if (open != null) {
                    open.page = keep.page;
                    foreach (var k in keep.Item2) open.expanded.Add(k);
                    open.bySimulation = keep.bySimulation;
                }
                return;
            }
            V5Brain.Want(0.5f);
            var typing = InputEvents.TextInput.IsTyping;
            var signature = Signature();
            if (signature != shown && !MotionUi.Capturing && !typing && !Popup.IsOpen) {
                shown = signature;
                Rebuild();
            }
            foreach (var update in live) update();
            foreach (var draw in gripDraws) draw();
            UpdateTabs();
            FitHeight();
            HoverHighlight();
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && MouseOver && !typing && !Sandbox.Running) Close();
        }

        private void FitHeight() {
            if (Interface.Interface.Root == null) return;
            var rootHeight = ((RectTransform)Interface.Interface.Root).rect.height;
            var room = rootHeight + window.anchoredPosition.y - 160f;
            var wanted = LayoutUtility.GetPreferredHeight(content);
            // The window's height: what the cards need, up to the size it was given (header, tabs and status: ~145).
            frameLayout.preferredHeight = Mathf.Clamp(wanted, 60f, Mathf.Max(120f, Mathf.Min(room, heightMax - 145f)));
            if (focusKey != null) {
                var card = cards.FirstOrDefault(c => c.device.Key == focusKey).rect;
                if (card != null) {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                    var corners = new Vector3[4];
                    card.GetWorldCorners(corners);
                    var top = -content.InverseTransformPoint(corners[1]).y;
                    var max = Mathf.Max(0f, content.rect.height - frame.rect.height);
                    content.anchoredPosition = new Vector2(0f, Mathf.Clamp(top - 8f, 0f, max));
                }
                focusKey = null;
            }
        }

        private V5Device hovered;

        private void HoverHighlight() {
            V5Device over = null;
            if (MouseOver)
                foreach (var (rect, device) in cards)
                    if (rect != null && RectTransformUtility.RectangleContainsScreenPoint(rect, MouseInput.Position, null)) over = device;
            if (over == hovered) return;
            hovered = over;
            if (over == null) {
                Highlights.Clear("devices");
                return;
            }
            var parts = over is SolenoidDevice s ? s.cylinders.Where(c => c != null).Select(c => c.gameObject).Concat(s.part != null ? new[] { s.part } : new GameObject[0])
                : over.part != null ? new[] { over.part } : new GameObject[0];
            Highlights.Set("devices", parts, Theme.Accent, true);
        }

        private void OnDestroy() {
            Highlights.Clear("devices");
            if (open == this) open = null;
        }

        // ------------------------------------------------------------------ the tabs (the Brain's top row)

        private TextMeshProUGUI devicesValue, wireValue, controllerValue, batteryValue;
        private Image batteryFill;
        private readonly List<(Page page, Image image)> tabImages = new();

        private void BuildTabs() {
            for (var i = tabs.childCount - 1; i >= 0; i--) Destroy(tabs.GetChild(i).gameObject);
            tabImages.Clear();
            devicesValue = Tab(Page.Devices, "Devices", "Smart ports 1-21, then a card per device: motors, sensors, solenoids.");
            wireValue = Tab(Page.ThreeWire, "3-Wire", "Ports A-H: what's on each, its mode and value, as the Brain's 3-Wire screen shows them.");
            controllerValue = Tab(Page.Controller, "Controller", "How the controller drives the robot: tank, arcade or split arcade.");
            // The battery (not a page).
            var tile = Ui.Image(tabs, "Battery", Theme.PanelRaised, 8f);
            Ui.Column(tile.rectTransform, 2f, new RectOffset(10, 10, 6, 6));
            Ui.Text(tile.transform, "Label", "Battery", Theme.FontSmall - 1f, Theme.TextDim).Size(-1, 16f);
            var row = Ui.Rect("Row", tile.transform);
            Ui.Row(row, 6f);
            row.Size(-1, 20f);
            var track = Ui.Image(row, "Track", Theme.Field, 3f);
            track.Size(-1, 10f, 1f);
            batteryFill = Ui.Image(track.transform, "Fill", Theme.Success, 3f);
            batteryFill.rectTransform.anchorMin = Vector2.zero;
            batteryFill.rectTransform.anchorMax = new Vector2(1f, 1f);
            batteryFill.rectTransform.offsetMin = batteryFill.rectTransform.offsetMax = Vector2.zero;
            batteryValue = Ui.Text(row, "Value", "100%", Theme.FontSmall, Theme.Text, Theme.Medium);
            batteryValue.Size(40f, 20f);
            Ui.Tip(tile, "Battery", null, "The V5 battery (1100 mAh): the motors drain it while the simulation runs.");
        }

        private TextMeshProUGUI Tab(Page which, string label, string tip) {
            var tile = Ui.Image(tabs, label, Theme.PanelRaised, 8f, true);
            var button = tile.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => {
                page = which;
                shown = null;
                Audio.Sounds.Play(Audio.Sounds.Tick);
            });
            tile.gameObject.AddComponent<HoverTint>();
            Ui.Column(tile.rectTransform, 2f, new RectOffset(10, 10, 6, 6));
            Ui.Text(tile.transform, "Label", label, Theme.FontSmall - 1f, Theme.TextDim).Size(-1, 16f);
            var value = Ui.Text(tile.transform, "Value", "", Theme.FontBody, Theme.Text, Theme.Medium);
            value.Size(-1, 20f);
            Ui.Tip(tile, label, null, tip);
            tabImages.Add((which, tile));
            return value;
        }

        private void UpdateTabs() {
            if (devicesValue == null) return;
            var devices = V5Brain.Devices;
            devicesValue.text = devices.Count(d => !d.threeWire) + " smart";
            var used = devices.Where(d => d.threeWire && d.OnBrain).Sum(d => d.ports);
            wireValue.text = used + " of 8";
            controllerValue.text = Controls.DriveModeNames[(int)Controls.Drive];
            var b = V5Brain.Battery;
            batteryValue.text = Mathf.RoundToInt(b * 100f) + "%";
            batteryFill.rectTransform.anchorMax = new Vector2(Mathf.Max(0.02f, b), 1f);
            batteryFill.color = b > 0.3f ? Theme.Success : b > 0.15f ? Theme.Warning : Theme.Danger;
            foreach (var (which, image) in tabImages) image.color = which == page ? Theme.Hover : Theme.PanelRaised;
            subtitle.text = devices.Count == 0 ? "no devices yet" : devices.Count + (devices.Count == 1 ? " device" : " devices");
            if (Sandbox.Running && Sandbox.Instance != null) {
                var (left, rightPower) = Sandbox.DrivePower();
                status.text = Sandbox.Instance.BodyCount + " bodies · drive " + Mathf.RoundToInt(left * 100f) + "% / " + Mathf.RoundToInt(rightPower * 100f) + "%"
                              + (Sandbox.Instance.Paused ? " · paused" : "");
            }
            else status.text = MotorControls.Controlled.Count > 0
                ? "Keys run " + string.Join(", ", MotorControls.Controlled.Select(m => m.Label)) + " · < and > run a motor by hand"
                : "Click a motor in the build to run it from its keys · < and > run it by hand";
        }

        // HoverTint resets a tile's color when the mouse leaves; the selected tab keeps its own.
        private class HoverTint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
            private Image image;
            public void OnPointerEnter(PointerEventData e) {
                if (image == null) image = GetComponent<Image>();
                if (image.color == Theme.PanelRaised) image.color = Color.Lerp(Theme.PanelRaised, Theme.Hover, 0.6f);
            }
            public void OnPointerExit(PointerEventData e) { }
        }

        // ------------------------------------------------------------------ pages

        private void Rebuild() {
            for (var i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
            live.Clear();
            cards.Clear();
            hovered = null;
            Highlights.Clear("devices");
            if (tabImages.Count == 0) BuildTabs();
            switch (page) {
                case Page.ThreeWire: ThreeWirePage(); break;
                case Page.Controller: ControllerPage(); break;
                default: DevicesPage(); break;
            }
        }

        private void Heading(string text) {
            var heading = Dialog.Heading(content, text);
            heading.margin = new Vector4(0, 6, 0, 0);
            heading.Size(-1, 24f);
        }

        private TextMeshProUGUI Note(Transform parent, string text, Color? color = null, float height = 18f) {
            var note = Ui.Text(parent, "Note", text, Theme.FontSmall, color ?? Theme.TextDim);
            note.textWrappingMode = TextWrappingModes.Normal;
            note.Size(-1, height);
            return note;
        }

        private void DevicesPage() {
            var devices = V5Brain.Devices;
            // The smart ports, 7 x 3, as the Brain shows them.
            var grid = Ui.Rect("Ports", content);
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2((width - 24f - 6f - 4f * 6f) / 7f, 44f);
            layout.spacing = new Vector2(4f, 4f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 7;
            grid.Size(-1, 44f * 3f + 8f);
            for (var p = 1; p <= 21; p++) {
                var device = devices.FirstOrDefault(d => !d.threeWire && d.port == p);
                PortTile(grid, p, device);
            }
            if (devices.Count == 0) {
                Note(content, "No motors, sensors or cylinders in the build yet. Add them from the parts list (Electronics, Pneumatics): each takes a port, as if plugged in; wiring them to the brain picks the port.", null, 52f);
                return;
            }
            Section("Motors", devices.Where(d => d is MotorDevice));
            Section("Sensors", devices.Where(d => !d.threeWire && !(d is MotorDevice)));
            Section("Pneumatics", devices.Where(d => d is SolenoidDevice));
            Section("3-wire devices", devices.Where(d => d.threeWire && !(d is SolenoidDevice)));
            var off = devices.Where(d => !d.OnBrain).ToList();
            if (off.Count > 0)
                Note(content, off.Count + (off.Count == 1 ? " device has" : " devices have") + " no free port on the brain (21 smart, 8 three-wire): a real robot couldn't plug " + (off.Count == 1 ? "it" : "them") + " in.", Theme.Warning, 36f);
        }

        // A section's cards in columns (as many as fit the window), each card going into the shortest one.
        private void Section(string title, IEnumerable<V5Device> devices) {
            var list = devices.ToList();
            if (list.Count == 0) return;
            Heading(title);
            var n = Mathf.Min(Columns, list.Count);
            if (n <= 1) {
                foreach (var d in list) Card(content, d);
                return;
            }
            var row = Ui.Rect(title + " Columns", content);
            var layout = Ui.Row(row, 6f, null, TextAnchor.UpperLeft);
            layout.childForceExpandWidth = true;
            var columns = new RectTransform[n];
            var heights = new float[n];
            for (var i = 0; i < n; i++) {
                columns[i] = Ui.Rect("Column " + (i + 1), row);
                Ui.Column(columns[i], 6f);
                var element = columns[i].gameObject.AddComponent<LayoutElement>();
                element.flexibleWidth = 1f;
                element.preferredWidth = 0f;
                element.minWidth = 0f;
            }
            foreach (var d in list) {
                var k = System.Array.IndexOf(heights, heights.Min());
                Card(columns[k], d);
                heights[k] += Estimate(d) + 6f;
            }
        }

        // About how tall a card is (to balance the columns).
        private float Estimate(V5Device d) {
            if (!expanded.Contains(d.Key)) return 42f;
            return d switch {
                MotorDevice => 520f,
                DistanceSensor => 210f,
                OpticalSensor => 180f,
                ShaftSensor => 190f,
                InertialSensor => 250f,
                SolenoidDevice s => 110f + 60f * s.cylinders.Count,
                PlainDevice => 80f,
                _ => 140f,
            };
        }

        private void PortTile(RectTransform grid, int port, V5Device device) {
            var tile = Ui.Image(grid, "Port " + port, device != null ? Theme.PanelRaised : Theme.Field, 6f, true);
            var number = Ui.Text(tile.transform, "Number", port.ToString(), Theme.FontSmall - 2f, device != null ? Theme.TextDim : Theme.TextFaint);
            number.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(5f, -1f), new Vector2(26f, 16f));
            if (device == null) {
                Ui.Tip(tile, "Port " + port, null, "Empty.");
                return;
            }
            var icon = Icon(tile.transform, device, 24f);
            icon.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(3f, -3f), new Vector2(24f, 24f));
            if (!device.wired) {
                var dot = Ui.Text(tile.transform, "Unwired", "○", Theme.FontSmall - 3f, Theme.TextFaint, null, TextAlignmentOptions.TopRight);
                dot.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-4f, -1f), new Vector2(14f, 14f));
            }
            Ui.Tip(tile, "Port " + port + ": " + device.Name, null, device.wired ? "Wired to this port." : "Not wired: it takes this port as if plugged in (wire it to the brain to pick its port).");
            var button = tile.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            var key = device.Key;
            button.onClick.AddListener(() => {
                expanded.Add(key);
                focusKey = key;
                shown = null;
            });
        }

        // ------------------------------------------------------------------ cards

        private void Card(RectTransform parent, V5Device d) {
            var card = Box(parent, d.Name, Theme.PanelRaised, 8f, true).rect;
            var column = Ui.Column(card, 6f, new RectOffset(8, 8, 6, 8));
            column.childForceExpandWidth = true;
            cards.Add((card, d));
            var isOpen = expanded.Contains(d.Key);

            // The folded row: port, color, name, reading (and a motor's < ■ >).
            var row = Ui.Image(card.transform, "Row", new Color(0f, 0f, 0f, 0f), 0f, true).rectTransform;
            Ui.Row(row, 5f);
            row.Size(-1, 30f);
            var toggle = row.gameObject.AddComponent<Button>();
            toggle.transition = Selectable.Transition.None;
            var key = d.Key;
            toggle.onClick.AddListener(() => {
                if (!expanded.Remove(key)) {
                    expanded.Add(key);
                    if (d is MotorDevice md && !Sandbox.Running) MotorControls.Control(new[] { md.motor }, true, false);
                }
                shown = null;
            });
            Badge(row, d);
            Icon(row, d, 18f).Size(18f, 18f);
            var name = Ui.Text(row, "Name", d.Name, Theme.FontBody, Theme.Text);
            name.Size(-1, 30f, 1f);
            name.GetComponent<LayoutElement>().minWidth = 56f;
            if (d is MotorDevice mc && MotorControls.IsControlled(mc.motor) && !Sandbox.Running)
                name.text = d.Name + "  <color=#5AA2FF><size=78%>keys</size></color>";
            var reading = Ui.Text(row, "Reading", d.Reading, Theme.FontSmall, Theme.TextDim, Theme.Medium, TextAlignmentOptions.MidlineRight);
            reading.Size(d is MotorDevice ? 42f : d is SolenoidDevice ? 76f : 104f, 30f);
            live.Add(() => reading.text = d.Reading);
            if (d is MotorDevice motor) ManualButtons(row, motor);
            if (d is SolenoidDevice solenoid) ValveSwitch(row, solenoid);
            Chevron(row, isOpen);
            if (!isOpen) return;

            switch (d) {
                case MotorDevice m: MotorDashboard(card, m); break;
                case DistanceSensor s: DistanceDashboard(card, s); break;
                case OpticalSensor s: OpticalDashboard(card, s); break;
                case ShaftSensor s: ShaftDashboard(card, s); break;
                case InertialSensor s: InertialDashboard(card, s); break;
                case SolenoidDevice s: SolenoidDashboard(card, s); break;
                case PlainDevice s: Note(card, s.Note); break;
                default: ThreeWireDashboard(card, d); break;
            }
            if (d.part != null && !(d is MotorDevice) && !(d is SolenoidDevice && d.part == null)) NameRow(card, d);
        }

        private void Badge(RectTransform row, V5Device d) {
            var badge = Ui.Image(row, "Port", d.threeWire ? new Color(0.98f, 0.73f, 0.23f, 0.16f) : Theme.Field, 5f);
            badge.Size(d.ports > 1 ? 44f : 32f, 24f);
            var text = Ui.Text(badge.transform, "Port", d.OnBrain ? d.PortLabel : "—", Theme.FontSmall, d.threeWire ? Theme.Warning : Theme.Text, Theme.Medium, TextAlignmentOptions.Center);
            text.rectTransform.Fill();
            if (!d.wired) text.color = Color.Lerp(text.color, Theme.TextFaint, 0.45f);
            Ui.Tip(badge, d.threeWire ? "3-wire port " + d.PortLabel : "Smart port " + d.PortLabel, null,
                !d.OnBrain ? "No free port on the brain." : d.wired ? "Wired to this port." : "Not wired: on this port as if plugged in (wire it to the brain to pick another).");
        }

        private void ManualButtons(RectTransform row, MotorDevice m) {
            void Step(float by) {
                if (m.motor == null) return;
                m.motor.Manual = by == 0f ? 0f : Mathf.Clamp(Mathf.Round((m.motor.Manual + by) * 4f) / 4f, -1f, 1f);
                if (Mathf.Abs(m.motor.Manual) < 0.01f) m.motor.Manual = 0f;
                Audio.Sounds.Play(Audio.Sounds.Tick);
            }
            var back = Ui.Button(row, "Reverse", "<", () => Step(-0.25f), false, 5f, Theme.FontBody);
            back.Size(24f, 26f);
            Ui.Tip(back, "Reverse", null, "Runs it backward by hand, 25% more each click (as the Brain's motor screen does).");
            var stop = Ui.Button(row, "Stop", "■", () => Step(0f), false, 5f, Theme.FontSmall);
            stop.Size(24f, 26f);
            Ui.Tip(stop, "Stop", null, "Stops running it by hand.");
            var forward = Ui.Button(row, "Forward", ">", () => Step(0.25f), false, 5f, Theme.FontBody);
            forward.Size(24f, 26f);
            Ui.Tip(forward, "Forward", null, "Runs it forward by hand, 25% more each click.");
            live.Add(() => {
                var manual = m.motor != null ? m.motor.Manual : 0f;
                back.targetGraphic.color = manual < -0.01f ? Theme.Accent : Theme.PanelRaised;
                forward.targetGraphic.color = manual > 0.01f ? Theme.Accent : Theme.PanelRaised;
            });
        }

        private void ValveSwitch(RectTransform row, SolenoidDevice s) {
            var button = Ui.Button(row, "Value", "Low (0)", null, false, 5f, Theme.FontSmall);
            button.Size(64f, 26f);
            button.onClick.AddListener(() => {
                s.Set(!s.High);
                Audio.Sounds.Play(Audio.Sounds.Tick);
            });
            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            Ui.Tip(button, "Digital Out", null, "Switches the valve: High sends air to extend the cylinders, Low (the default) to retract them.");
            live.Add(() => {
                var high = s.High;
                label.text = high ? "High (1)" : "Low (0)";
                button.targetGraphic.color = high ? Theme.Accent : Theme.PanelRaised;
            });
        }

        // A row of value tiles; each value is read every frame.
        private RectTransform TileRow(RectTransform parent, float height = 50f) {
            var row = Ui.Rect("Tiles", parent);
            var layout = Ui.Row(row, 6f);
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;   // tiles fill the row's height
            row.Size(-1, height);
            return row;
        }

        private (Image tile, TextMeshProUGUI value) Tile(RectTransform row, string label, Func<string> value, float flexible = 1f, string tip = null) {
            var (rect, tile) = Box(row, label, Theme.Field, 6f, tip != null);
            rect.Size(-1, -1, flexible);
            var column = Ui.Column(rect, 0f, new RectOffset(8, 8, 4, 4));
            column.childForceExpandWidth = true;
            Ui.Text(rect, "Label", label, Theme.FontSmall - 2f, Theme.TextDim).Size(-1, 15f);
            var text = Ui.Text(rect, "Value", value(), Theme.FontBody + 1f, Theme.Text, Theme.Medium);
            text.Size(-1, 24f);
            live.Add(() => text.text = value());
            if (tip != null) Ui.Tip(tile, label, null, tip);
            return (tile, text);
        }

        private RectTransform ButtonRow(RectTransform parent) {
            var row = Ui.Rect("Buttons", parent);
            Ui.Row(row, 6f);
            row.Size(-1, 28f);
            return row;
        }

        private void NameRow(RectTransform card, V5Device d) {
            var row = ButtonRow(card);
            Ui.Text(row, "Label", "Name", Theme.FontSmall, Theme.TextDim).Size(44f, 28f);
            var field = Ui.Field(row, "Name", Controls.GivenName(d.part) ?? "", value => Controls.SetName(d.part, value), Theme.FontSmall);
            field.Size(-1, 26f, 1f);
            Ui.Tip(field, "Name", null, "A name for it (\"Left drive\", \"Intake sensor\"); empty: " + V5Device.KindName(d.kind) + ".");
        }

        // A box with a rounded background that doesn't size it (a rounded image reports its sprite's 66 px as its
        // size): the box is laid out by its contents.
        private static (RectTransform rect, Image image) Box(Transform parent, string name, Color color, float radius, bool raycast = false) {
            var rect = Ui.Rect(name, parent);
            var image = Ui.Image(rect, "Background", color, radius, raycast);
            image.rectTransform.Fill();
            image.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            return (rect, image);
        }

        // A device's little icon (drawn once it has its size).
        private RectTransform Icon(Transform parent, V5Device d, float size) {
            var holder = Ui.Image(parent, "Icon", new Color(0f, 0f, 0f, 0f), 0f, true).rectTransform;
            holder.sizeDelta = new Vector2(size, size);
            var shapes = ShapeGraphic.Create(holder);
            var drawn = false;
            live.Add(() => {
                var r = shapes.rectTransform.rect;
                if (drawn || r.width <= 0f) return;
                drawn = true;
                DeviceIcons.Draw(shapes, r, d.kind, d.Tint);
            });
            Ui.Tip(holder, V5Device.KindName(d.kind));
            return holder;
        }

        // Resizing: an edge or corner dragged changes the width (the left edge: the window grows leftward) and the
        // most height it takes.
        private void Grip(string name, Vector2 min, Vector2 max, int dx, int dy) {
            var grip = Ui.Image(window, name + " Grip", new Color(1f, 1f, 1f, 0f), 0f, true).rectTransform;
            grip.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            grip.anchorMin = min;
            grip.anchorMax = max;
            grip.pivot = new Vector2(0.5f, 0.5f);
            const float T = 10f;
            grip.sizeDelta = new Vector2(dx != 0 && dy == 0 ? T : dx != 0 ? 18f : -24f, dy != 0 && dx == 0 ? T : dy != 0 ? 18f : -24f);
            grip.anchoredPosition = Vector2.zero;
            var resizer = grip.gameObject.AddComponent<Resizer>();
            resizer.owner = this;
            resizer.dx = dx;
            resizer.dy = dy;
            if (dx != 0 && dy != 0) {   // a corner shows a grip
                var shapes = ShapeGraphic.Create(grip);
                var drawn = false;
                gripDraws.Add(() => {
                    var r = shapes.rectTransform.rect;
                    if (drawn || r.width <= 0f) return;
                    drawn = true;
                    var corner = new Vector2(dx < 0 ? r.center.x - 2f : r.center.x + 2f, r.center.y - 2f);
                    for (var i = 1; i <= 2; i++)
                        shapes.Line(corner + new Vector2(0f, 4.5f * i), corner + new Vector2(-dx * 4.5f * i, 0f), Theme.TextFaint, 1.2f);
                });
            }
        }

        private readonly List<Action> gripDraws = new();

        private void Resize(Vector2 delta, int dx, int dy) {
            var root = ((RectTransform)Interface.Interface.Root).rect;
            if (dx < 0) width = Mathf.Clamp(width - delta.x, MinWidth, root.width - 20f);
            if (dx > 0) {
                var before = width;
                width = Mathf.Clamp(width + delta.x, MinWidth, root.width - 20f);
                window.anchoredPosition += new Vector2(width - before, 0f);
            }
            if (dy < 0) heightMax = Mathf.Clamp(heightMax - delta.y, MinHeight, root.height);
            window.sizeDelta = new Vector2(width, window.sizeDelta.y);
            KeepOnScreen();
        }

        private class Resizer : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler {
            public DevicesPanel owner;
            public int dx, dy;
            private Image image;

            public void OnBeginDrag(PointerEventData e) {
                if (dy != 0) owner.heightMax = Mathf.Max(MinHeight, owner.window.rect.height);
            }

            public void OnDrag(PointerEventData e) =>
                owner.Resize(e.delta / Mathf.Max(Interface.Interface.Root.lossyScale.x, 0.0001f), dx, dy);

            public void OnEndDrag(PointerEventData e) {
                PlayerPrefs.SetString(SizeKey, owner.width.ToString(CultureInfo.InvariantCulture) + "," + owner.heightMax.ToString(CultureInfo.InvariantCulture));
                if (!owner.right) {
                    var p = owner.window.anchoredPosition;
                    PlayerPrefs.SetString(PositionKey, p.x.ToString(CultureInfo.InvariantCulture) + "," + p.y.ToString(CultureInfo.InvariantCulture));
                }
            }

            public void OnPointerEnter(PointerEventData e) {
                if (image == null) image = GetComponent<Image>();
                image.color = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.35f);
            }

            public void OnPointerExit(PointerEventData e) {
                if (image == null) image = GetComponent<Image>();
                image.color = new Color(1f, 1f, 1f, 0f);
            }
        }

        // A small arrow: down when folded, up when open.
        private void Chevron(RectTransform row, bool up) {
            var holder = Ui.Rect("Open", row);
            holder.Size(16f, 30f);
            var shapes = ShapeGraphic.Create(holder);
            var drawn = false;
            live.Add(() => {
                var r = shapes.rectTransform.rect;
                if (drawn || r.width <= 0f) return;
                drawn = true;
                var c = r.center;
                var s = up ? -1f : 1f;
                shapes.Line(c + new Vector2(-5f, 2.5f * s), c + new Vector2(0f, -2.5f * s), Theme.TextDim, 1.8f);
                shapes.Line(c + new Vector2(0f, -2.5f * s), c + new Vector2(5f, 2.5f * s), Theme.TextDim, 1.8f);
            });
        }

        private static string F(float v, string format) => v.ToString(format, CultureInfo.InvariantCulture);
    }
}
