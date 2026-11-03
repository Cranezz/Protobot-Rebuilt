using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Interface {
    // V2: the paint color picker (as in the earlier V2): a saturation/brightness square with a hue bar beside it,
    // hex and R G B boxes, team colors and the recently used colors. Opens under the tool options bar; every
    // change applies straight away. Escape or a click outside closes it.
    // V2: the Paint tool's picker floats instead (OpenFloating): it opens with the tool, its title bar drags it
    // anywhere on the screen (the place is remembered), and "-" folds it into a small bar showing the current
    // color; clicking the bar opens it again.
    public class ColorPickerPanel : MonoBehaviour {
        private static ColorPickerPanel open;

        public static readonly Color[] TeamColors = {
            Hex("C8102E"), Hex("E8641B"), Hex("F2B705"), Hex("2E9E44"), Hex("1F4FBF"), Hex("6A3FA0"),
            Hex("E04E9A"), Hex("1A9E9E"), Hex("F2F2F2"), Hex("B9BEC4"), Hex("5D6166"), Hex("1A1A1A"),
        };

        public static Color Hex(string code) => ColorUtility.TryParseHtmlString("#" + code, out var c) ? c : Color.magenta;

        private const int SquareSize = 96;
        private RectTransform panel, squareHandle, hueHandle;
        private Texture2D squareTexture;
        private Image preview;
        private TMP_InputField hex;
        private readonly TMP_InputField[] rgb = new TMP_InputField[3];
        private float hue, saturation, value;
        private float drawnHue = -1f;
        private Action<Color> changed;
        private bool syncing;

        public static bool IsOpen => open != null;

        private bool floating, minimized;
        private RectTransform window, header;
        private Image headerSwatch;
        private TextMeshProUGUI headerTitle;
        private Button minimizeButton;
        private const string PositionKey = "V2.PaintPanelPosition", MinimizedKey = "V2.PaintPanelMinimized";

        /// <summary>The floating picker (the Paint tool's): draggable, folds into a bar; stays until closed.</summary>
        public static void OpenFloating(string title, Color start, Action<Color> onChange) {
            if (open != null && open.floating) {
                open.changed = onChange;
                open.SetColor(start, true, false);
                return;
            }
            Close();
            var go = Ui.Rect("Color Picker", Interface.Root).Fill().gameObject;
            open = go.AddComponent<ColorPickerPanel>();
            open.floating = true;
            open.changed = onChange;
            open.BuildFloating(title);
            open.SetColor(start, true, false);
        }

        /// <summary>Sets the picker's color from outside (another part's color was picked up).</summary>
        public static void Show(Color color) {
            if (open != null) open.SetColor(color, true, false);
        }

        public static bool IsFloating => open != null && open.floating;

        private void BuildFloating(string title) {
            // The fullscreen holder mustn't take clicks meant for the scene.
            window = Ui.Rect("Window", transform);
            window.anchorMin = window.anchorMax = new Vector2(0f, 1f);
            window.pivot = new Vector2(0f, 1f);
            window.sizeDelta = new Vector2(330f, 404f + 36f);
            var saved = PlayerPrefs.GetString(PositionKey, "");
            var parts = saved.Split(',');
            window.anchoredPosition = parts.Length == 2 && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
                                      && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y)
                ? new Vector2(x, y) : new Vector2(82f, -(Layout.TopBarBottom + 70f));

            header = Ui.Panel(window, "Header", Theme.PanelRaised, 10f).rectTransform;
            header.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(330f, 34f));
            header.GetChild(0).GetComponent<Image>().raycastTarget = true;
            header.gameObject.AddComponent<Mover>().owner = this;
            headerSwatch = Ui.Image(header, "Swatch", Color.white, 4f);
            headerSwatch.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(22f, 18f));
            headerTitle = Ui.Text(header, "Title", title, Theme.FontBody, Theme.Text, Theme.Medium);
            headerTitle.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(220f, 30f));
            headerTitle.raycastTarget = false;
            minimizeButton = Ui.Button(header, "Minimize", "-", () => SetMinimized(!minimized), false, 5f, Theme.FontTitle);
            ((RectTransform)minimizeButton.transform).Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-6f, 0f), new Vector2(28f, 26f));
            Ui.Tip(minimizeButton, "Minimize", null, "Folds the picker into this bar; click the bar to open it again.");

            var anchor = Ui.Rect("Anchor", window);
            anchor.anchorMin = anchor.anchorMax = new Vector2(0f, 1f);
            anchor.pivot = new Vector2(0f, 1f);
            anchor.sizeDelta = Vector2.zero;
            Build(anchor);
            panel.SetParent(window, false);
            panel.anchorMin = panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 1f);
            panel.anchoredPosition = new Vector2(0f, -38f);
            SetMinimized(PlayerPrefs.GetInt(MinimizedKey, 0) == 1);
            KeepOnScreen();
        }

        private void SetMinimized(bool value) {
            minimized = value;
            PlayerPrefs.SetInt(MinimizedKey, value ? 1 : 0);
            panel.gameObject.SetActive(!value);
            header.sizeDelta = new Vector2(value ? 200f : 330f, 34f);
            minimizeButton.GetComponentInChildren<TextMeshProUGUI>().text = value ? "+" : "-";
            var tip = minimizeButton.GetComponent<TipSource>();
            if (tip != null) tip.title = value ? "Open the color picker" : "Minimize";
        }

        private void KeepOnScreen() {
            if (window == null || Interface.Root == null) return;
            var root = Interface.Root.rect;
            var p = window.anchoredPosition;
            p.x = Mathf.Clamp(p.x, 0f, root.width - 120f);
            p.y = Mathf.Clamp(p.y, -root.height + 34f, 0f);
            window.anchoredPosition = p;
        }

        // Dragging the title bar moves the picker; a click (no drag) on the folded bar opens it.
        private class Mover : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler {
            public ColorPickerPanel owner;
            private bool moved;

            public void OnPointerDown(PointerEventData e) => moved = false;

            public void OnDrag(PointerEventData e) {
                moved = true;
                var scale = Interface.Root.lossyScale.x;
                owner.window.anchoredPosition += e.delta / Mathf.Max(scale, 0.0001f);
                owner.KeepOnScreen();
            }

            public void OnPointerUp(PointerEventData e) {
                var p = owner.window.anchoredPosition;
                PlayerPrefs.SetString(PositionKey, p.x.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + p.y.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (!moved && owner.minimized) owner.SetMinimized(false);
            }
        }

        public static void Toggle(RectTransform anchor, Color start, Action<Color> onChange) {
            if (open != null) {
                Close();
                return;
            }
            var go = Ui.Rect("Color Picker", Interface.Root).Fill().gameObject;
            open = go.AddComponent<ColorPickerPanel>();
            open.changed = onChange;
            open.Build(anchor);
            open.SetColor(start, true, false);
        }

        public static void Close() {
            if (open != null) Destroy(open.gameObject);
            open = null;
        }

        private void Build(RectTransform anchor) {
            panel = Ui.Panel(transform, "Panel", Theme.Panel, 10f).rectTransform;
            var corners = new Vector3[4];
            anchor.GetWorldCorners(corners);
            var below = Layout.FromScreen(corners[0]);
            panel.Place(Vector2.zero, new Vector2(0f, 1f), below + new Vector2(-12f, -10f), new Vector2(330f, 404f));

            // Saturation / brightness square and hue bar.
            squareTexture = new Texture2D(SquareSize, SquareSize, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var square = Ui.Rect("Square", panel).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -14f), new Vector2(240f, 170f));
            square.gameObject.AddComponent<RawImage>().texture = squareTexture;
            Drag.Add(square, p => SetHsv(hue, p.x, p.y));
            squareHandle = Ui.Image(square, "Handle", Color.white, 7f).rectTransform;
            squareHandle.sizeDelta = new Vector2(14f, 14f);
            var ring = squareHandle.gameObject.AddComponent<Outline>();
            ring.effectColor = new Color(0f, 0f, 0f, 0.6f);

            var hueTexture = new Texture2D(1, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (var i = 0; i < 128; i++)
                hueTexture.SetPixel(0, i, Color.HSVToRGB(i / 127f, 1f, 1f));
            hueTexture.Apply();
            var hueBar = Ui.Rect("Hue", panel).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(266f, -14f), new Vector2(22f, 170f));
            hueBar.gameObject.AddComponent<RawImage>().texture = hueTexture;
            Drag.Add(hueBar, p => SetHsv(p.y, saturation, value));
            hueHandle = Ui.Image(hueBar, "Handle", Color.white, 2f).rectTransform;
            hueHandle.sizeDelta = new Vector2(28f, 5f);

            preview = Ui.Image(panel, "Preview", Color.white, 5f);
            preview.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(298f, -14f), new Vector2(18f, 170f));

            // Hex and R G B.
            var row = Ui.Rect("Values", panel).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -196f), new Vector2(302f, 30f));
            Ui.Row(row, 6f);
            hex = Ui.Field(row, "Hex", "", text => {
                var code = text.Trim();
                if (!code.StartsWith("#")) code = "#" + code;
                if (ColorUtility.TryParseHtmlString(code, out var c)) SetColor(c, true, true);
                else Sync(false);
            }, Theme.FontSmall);
            hex.Size(92f, 30f);
            hex.characterLimit = 7;
            string[] names = { "R", "G", "B" };
            for (var i = 0; i < 3; i++) {
                var channel = i;
                var label = Ui.Text(row, names[i], names[i], Theme.FontSmall, Theme.Axis(i), Theme.Medium, TextAlignmentOptions.MidlineRight);
                label.Size(14f, 30f);
                rgb[i] = Ui.Field(row, names[i] + " Value", "", text => {
                    if (!int.TryParse(text, out var n)) { Sync(false); return; }
                    var c = Current;
                    c[channel] = Mathf.Clamp(n, 0, 255) / 255f;
                    SetColor(c, true, true);
                }, Theme.FontSmall, TextAlignmentOptions.Center);
                rgb[i].contentType = TMP_InputField.ContentType.IntegerNumber;
                rgb[i].characterLimit = 3;
                rgb[i].Size(46f, 30f);
            }

            Swatches("Team colors", TeamColors, -236f);
            Swatches("Recent", Theme.RecentColors.ToArray(), -318f);
        }

        private void Swatches(string title, Color[] colors, float top) {
            var label = Ui.Text(panel, title, title, Theme.FontSmall, Theme.TextDim, Theme.Medium);
            label.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, top), new Vector2(200f, 18f));
            if (colors.Length == 0) {
                var none = Ui.Text(panel, "None", "Colors you use show up here", Theme.FontSmall, Theme.TextFaint);
                none.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, top - 24f), new Vector2(300f, 24f));
                return;
            }
            for (var i = 0; i < colors.Length && i < 16; i++) {
                var color = colors[i];
                var swatch = Ui.Image(panel, "Swatch", color, 5f, true);
                swatch.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(14f + (i % 8) * 38f, top - 24f - (i / 8) * 30f), new Vector2(32f, 24f));
                var button = swatch.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => SetColor(color, true, true));
                Ui.Tip(swatch, "#" + ColorUtility.ToHtmlStringRGB(color));
            }
        }

        private Color Current => Color.HSVToRGB(hue, saturation, value);

        private void SetColor(Color color, bool updateHsv, bool notify) {
            Color.RGBToHSV(color, out var h, out var s, out var v);
            if (s > 0.001f && v > 0.001f) hue = h;  // keep the hue for greys, so the square doesn't jump
            saturation = s;
            value = v;
            Sync(notify);
        }

        private void SetHsv(float h, float s, float v) {
            hue = Mathf.Clamp01(h);
            saturation = Mathf.Clamp01(s);
            value = Mathf.Clamp01(v);
            Sync(true);
        }

        private void Sync(bool notify) {
            if (syncing) return;
            syncing = true;
            if (!Mathf.Approximately(drawnHue, hue)) {
                drawnHue = hue;
                var pixels = new Color32[SquareSize * SquareSize];
                for (var y = 0; y < SquareSize; y++)
                    for (var x = 0; x < SquareSize; x++)
                        pixels[y * SquareSize + x] = Color.HSVToRGB(hue, x / (SquareSize - 1f), y / (SquareSize - 1f));
                squareTexture.SetPixels32(pixels);
                squareTexture.Apply();
            }
            var current = Current;
            squareHandle.anchorMin = squareHandle.anchorMax = new Vector2(saturation, value);
            hueHandle.anchorMin = hueHandle.anchorMax = new Vector2(0.5f, hue);
            preview.color = current;
            if (headerSwatch != null) headerSwatch.color = current;
            hex.SetTextWithoutNotify("#" + ColorUtility.ToHtmlStringRGB(current));
            for (var i = 0; i < 3; i++)
                rgb[i].SetTextWithoutNotify(Mathf.RoundToInt(current[i] * 255f).ToString());
            syncing = false;
            if (notify) changed?.Invoke(current);
        }

        private void Update() {
            if (floating) return;  // the Paint tool's picker stays until the tool closes it
            var mouse = Mouse.current;
            var outside = mouse != null && mouse.leftButton.wasPressedThisFrame
                          && !RectTransformUtility.RectangleContainsScreenPoint(panel, mouse.position.ReadValue())
                          && !OverPaintOptions(mouse.position.ReadValue());
            if (outside || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame))
                Close();
        }

        // The swatch that opened the picker toggles it itself.
        private static bool OverPaintOptions(Vector2 screen) {
            var current = EventSystem.current;
            if (current == null) return false;
            var data = new PointerEventData(current) { position = screen };
            var results = new System.Collections.Generic.List<RaycastResult>();
            current.RaycastAll(data, results);
            return results.Count > 0 && results[0].gameObject.GetComponentInParent<ToolOptions>() != null;
        }

        private void OnDestroy() {
            if (squareTexture != null) Destroy(squareTexture);
        }

        // Where on a rect the pointer is pressed or dragged, 0..1 each way.
        private class Drag : MonoBehaviour, IPointerDownHandler, IDragHandler {
            private Action<Vector2> onPosition;

            public static void Add(RectTransform rect, Action<Vector2> onPosition) {
                rect.gameObject.AddComponent<Drag>().onPosition = onPosition;
                if (rect.TryGetComponent(out Graphic g)) g.raycastTarget = true;
            }

            public void OnPointerDown(PointerEventData e) => Report(e);
            public void OnDrag(PointerEventData e) => Report(e);

            private void Report(PointerEventData e) {
                var rect = (RectTransform)transform;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, e.position, e.pressEventCamera, out var local))
                    return;
                var r = rect.rect;
                onPosition?.Invoke(new Vector2(Mathf.Clamp01((local.x - r.xMin) / r.width), Mathf.Clamp01((local.y - r.yMin) / r.height)));
            }
        }
    }
}
