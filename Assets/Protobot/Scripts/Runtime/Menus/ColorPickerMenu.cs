using System;
using System.Collections.Generic;
using System.Globalization;
using Protobot.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Menus
{
    /// <summary>
    /// Paint colour picker: a saturation/brightness square with a hue bar beside it (like an image editor),
    /// RGB sliders with exact values, a hex code box, old vs new swatches, and the last 20 colours used
    /// (5 columns × 4 rows). Every control stays in sync with the others.
    /// </summary>
    public class ColorPickerMenu : MonoBehaviour
    {
        const int SquareResolution = 96;

        static readonly Color[] Presets =
        {
            Hex("C8102E"), Hex("E8641B"), Hex("F2B705"), Hex("2E9E44"), Hex("1F4FBF"), Hex("6A3FA0"),
            Hex("E04E9A"), Hex("1A9E9E"), Hex("F2F2F2"), Hex("B9BEC4"), Hex("5D6166"), Hex("1A1A1A"),
        };

        static Color Hex(string code) => ColorUtility.TryParseHtmlString("#" + code, out var c) ? c : Color.magenta;

        [SerializeField] UiTheme theme;

        GameObject root;
        Texture2D squareTexture;
        RawImage square;
        RectTransform squareHandle;
        RectTransform hueHandle;
        Image oldSwatch;
        Image newSwatch;
        readonly Slider[] rgb = new Slider[3];
        readonly TMP_InputField[] rgbFields = new TMP_InputField[3];
        TMP_InputField hex;
        readonly List<Image> recentSwatches = new();
        readonly List<Button> recentButtons = new();

        float hue, saturation, value;
        Color current;
        Action<Color> onChosen;
        IReadOnlyList<Color> recent;
        bool syncing;

        public bool IsOpen => root != null && root.activeSelf;

        void Awake()
        {
            UiFactory.Canvas(gameObject, 25);
            Build();
            root.SetActive(false);
        }

        public void Open(Color start, IReadOnlyList<Color> recentColors, Action<Color> chosen)
        {
            onChosen = chosen;
            recent = recentColors;
            oldSwatch.color = start;
            root.SetActive(true);
            MenuState.Open(this);
            SetColor(start, true);
            RefreshRecent();
        }

        void Close()
        {
            root.SetActive(false);
            MenuState.Close(this);
            EventSystem.current?.SetSelectedGameObject(null);
        }

        void Update()
        {
            if (IsOpen && Keyboard.current.escapeKey.wasPressedThisFrame && MenuState.ChangedFrame != Time.frameCount)
                Close();
        }

        // ---------------------------------------------------------------- state

        void SetColor(Color color, bool updateHsv)
        {
            current = new Color(Mathf.Clamp01(color.r), Mathf.Clamp01(color.g), Mathf.Clamp01(color.b), 1f);
            if (updateHsv)
            {
                Color.RGBToHSV(current, out var h, out var s, out var v);
                // Keep the hue when the colour is grey/black, so the square doesn't jump.
                if (s > 0.001f && v > 0.001f)
                    hue = h;
                saturation = s;
                value = v;
            }
            Sync();
        }

        void SetHsv(float h, float s, float v)
        {
            hue = Mathf.Clamp01(h);
            saturation = Mathf.Clamp01(s);
            value = Mathf.Clamp01(v);
            current = Color.HSVToRGB(hue, saturation, value);
            Sync();
        }

        void Sync()
        {
            if (syncing)
                return;
            syncing = true;
            RedrawSquare();
            squareHandle.anchorMin = squareHandle.anchorMax = new Vector2(saturation, value);
            hueHandle.anchorMin = hueHandle.anchorMax = new Vector2(0.5f, hue);
            newSwatch.color = current;
            for (var i = 0; i < 3; i++)
            {
                var byteValue = Mathf.RoundToInt(current[i] * 255f);
                rgb[i].SetValueWithoutNotify(byteValue);
                rgbFields[i].SetTextWithoutNotify(byteValue.ToString());
            }
            hex.SetTextWithoutNotify("#" + ColorUtility.ToHtmlStringRGB(current));
            syncing = false;
        }

        float drawnHue = -1f;

        void RedrawSquare()
        {
            if (Mathf.Approximately(drawnHue, hue))
                return;
            drawnHue = hue;
            var pixels = new Color32[SquareResolution * SquareResolution];
            for (var y = 0; y < SquareResolution; y++)
                for (var x = 0; x < SquareResolution; x++)
                    pixels[y * SquareResolution + x] = Color.HSVToRGB(hue, x / (SquareResolution - 1f), y / (SquareResolution - 1f));
            squareTexture.SetPixels32(pixels);
            squareTexture.Apply();
        }

        void RefreshRecent()
        {
            for (var i = 0; i < recentSwatches.Count; i++)
            {
                var has = recent != null && i < recent.Count;
                recentSwatches[i].color = has ? recent[i] : new Color(1f, 1f, 1f, 0.06f);
                recentButtons[i].interactable = has;
            }
        }

        // ---------------------------------------------------------------- building

        void Build()
        {
            var dim = UiFactory.Image(transform, "Dim", new Color(0f, 0f, 0f, 0.4f), null, true);
            dim.rectTransform.Fill();
            root = dim.gameObject;
            var panel = UiFactory.Panel(dim.transform, "Panel", theme, theme.panel, true).rectTransform;
            panel.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860f, 740f));

            var stripes = UiFactory.Hazard(panel, "Stripes", theme);
            stripes.rectTransform.anchorMin = new Vector2(0f, 1f);
            stripes.rectTransform.anchorMax = new Vector2(1f, 1f);
            stripes.rectTransform.offsetMin = new Vector2(14f, -24f);
            stripes.rectTransform.offsetMax = new Vector2(-14f, -12f);
            var title = UiFactory.Text(panel, "Title", theme, "PAINT COLOUR", 44f, theme.text, true);
            title.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -30f), new Vector2(500f, 56f));

            // Saturation / brightness square.
            squareTexture = new Texture2D(SquareResolution, SquareResolution, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var squareRect = UiFactory.Rect("Square", panel).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -104f), new Vector2(340f, 340f));
            square = squareRect.gameObject.AddComponent<RawImage>();
            square.texture = squareTexture;
            DragArea.Add(squareRect, p => SetHsv(hue, p.x, p.y));
            squareHandle = UiFactory.Image(squareRect, "Handle", Color.white, theme.roundedOutline).rectTransform;
            squareHandle.sizeDelta = new Vector2(22f, 22f);

            // Hue bar.
            var hueTexture = new Texture2D(1, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (var i = 0; i < 128; i++)
                hueTexture.SetPixel(0, i, Color.HSVToRGB(i / 127f, 1f, 1f));
            hueTexture.Apply();
            var hueRect = UiFactory.Rect("Hue", panel).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(390f, -104f), new Vector2(40f, 340f));
            hueRect.gameObject.AddComponent<RawImage>().texture = hueTexture;
            DragArea.Add(hueRect, p => SetHsv(p.y, saturation, value));
            hueHandle = UiFactory.Image(hueRect, "Handle", Color.white, theme.roundedOutline).rectTransform;
            hueHandle.sizeDelta = new Vector2(52f, 14f);

            // Old / new swatches.
            var swatchLabel = UiFactory.Text(panel, "SwatchLabel", theme, "OLD  /  NEW", 18f, theme.textDim, true);
            swatchLabel.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(460f, -100f), new Vector2(360f, 24f));
            oldSwatch = UiFactory.Image(panel, "Old", Color.white, theme.rounded);
            oldSwatch.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(460f, -128f), new Vector2(175f, 56f));
            newSwatch = UiFactory.Image(panel, "New", Color.white, theme.rounded);
            newSwatch.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(643f, -128f), new Vector2(175f, 56f));

            // RGB sliders and hex.
            string[] names = { "R", "G", "B" };
            for (var i = 0; i < 3; i++)
            {
                var channel = i;
                var y = -206f - i * 52f;
                var label = UiFactory.Text(panel, names[i], theme, names[i], 24f, theme.text, true);
                label.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(460f, y - 6f), new Vector2(30f, 36f));
                var sliderRect = UiFactory.Rect(names[i] + "Slider", panel).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(490f, y), new Vector2(230f, 44f));
                rgb[i] = UiFactory.Slider(sliderRect, "Slider", theme, 0f, 255f, 0f, v =>
                {
                    var c = current;
                    c[channel] = Mathf.Round(v) / 255f;
                    SetColor(c, true);
                });
                ((RectTransform)rgb[i].transform).Fill();
                rgb[i].wholeNumbers = true;
                rgbFields[i] = UiFactory.InputField(panel, names[i] + "Field", theme, "", 22f);
                ((RectTransform)rgbFields[i].transform).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(732f, y), new Vector2(86f, 44f));
                rgbFields[i].contentType = TMP_InputField.ContentType.IntegerNumber;
                rgbFields[i].onEndEdit.AddListener(text =>
                {
                    if (!int.TryParse(text, out var n))
                        return;
                    var c = current;
                    c[channel] = Mathf.Clamp(n, 0, 255) / 255f;
                    SetColor(c, true);
                });
            }
            var hexLabel = UiFactory.Text(panel, "HexLabel", theme, "HEX", 24f, theme.text, true);
            hexLabel.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(460f, -368f), new Vector2(60f, 36f));
            hex = UiFactory.InputField(panel, "Hex", theme, "#RRGGBB", 24f);
            ((RectTransform)hex.transform).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(530f, -362f), new Vector2(288f, 48f));
            hex.characterLimit = 7;
            hex.onEndEdit.AddListener(text =>
            {
                var code = text.Trim();
                if (!code.StartsWith("#"))
                    code = "#" + code;
                if (ColorUtility.TryParseHtmlString(code, out var c))
                    SetColor(c, true);
                else
                    Sync();
            });

            // Common team colours, one click away.
            var presetLabel = UiFactory.Text(panel, "PresetLabel", theme, "TEAM COLOURS", 18f, theme.textDim, true);
            presetLabel.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(460f, -432f), new Vector2(300f, 24f));
            for (var i = 0; i < Presets.Length; i++)
            {
                var preset = Presets[i];
                var swatch = UiFactory.Image(panel, "Preset" + i, preset, theme.rounded, true);
                swatch.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(460f + (i % 6) * 60f, -462f - (i / 6) * 56f), new Vector2(50f, 48f));
                var button = swatch.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => SetColor(preset, true));
            }

            // Recent colours: 5 × 4.
            var recentLabel = UiFactory.Text(panel, "RecentLabel", theme, "RECENT", 18f, theme.textDim, true);
            recentLabel.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -460f), new Vector2(200f, 24f));
            for (var i = 0; i < 20; i++)
            {
                var col = i % 5;
                var row = i / 5;
                var index = i;
                var swatch = UiFactory.Image(panel, "Recent" + i, Color.white, theme.rounded, true);
                swatch.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f + col * 52f, -490f - row * 52f), new Vector2(44f, 44f));
                var button = swatch.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() =>
                {
                    if (recent != null && index < recent.Count)
                        SetColor(recent[index], true);
                });
                recentSwatches.Add(swatch);
                recentButtons.Add(button);
            }

            var use = UiFactory.Button(panel, "Use", theme, "USE COLOUR", () =>
            {
                onChosen?.Invoke(current);
                Close();
            }, true, 26f);
            ((RectTransform)use.transform).Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-32f, 32f), new Vector2(240f, 62f));
            var cancel = UiFactory.Button(panel, "Cancel", theme, "CANCEL", Close, false, 24f);
            ((RectTransform)cancel.transform).Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-288f, 32f), new Vector2(170f, 62f));
        }
    }

    /// <summary>Reports where on a rect the pointer is pressed or dragged, as 0..1 in each direction.</summary>
    public class DragArea : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        Action<Vector2> onPosition;

        public static void Add(RectTransform rect, Action<Vector2> onPosition)
        {
            var area = rect.gameObject.AddComponent<DragArea>();
            area.onPosition = onPosition;
            if (rect.TryGetComponent(out Graphic g))
                g.raycastTarget = true;
        }

        public void OnPointerDown(PointerEventData e) => Report(e);
        public void OnDrag(PointerEventData e) => Report(e);

        void Report(PointerEventData e)
        {
            var rect = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, e.position, e.pressEventCamera, out var local))
                return;
            var r = rect.rect;
            onPosition?.Invoke(new Vector2(Mathf.Clamp01((local.x - r.xMin) / r.width), Mathf.Clamp01((local.y - r.yMin) / r.height)));
        }
    }
}
