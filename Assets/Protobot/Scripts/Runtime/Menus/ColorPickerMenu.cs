using System;
using System.Collections.Generic;
using System.Globalization;
using Protobot.Core;
using Protobot.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Menus
{
    /// <summary>
    /// Paint colour picker: a saturation/brightness square with a hue bar beside it (like an image editor),
    /// RGB sliders with exact values, a hex code box, old vs new swatches, team colours and the last 20
    /// colours used (5 columns × 4 rows). Every control stays in sync with the others. In part mode
    /// (<see cref="OpenForPart"/>) it also shows the part and its colours, and edits them one at a time.
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
            LeavePartMode();
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
            LeavePartMode();
            root.SetActive(false);
            MenuState.Close(this);
            EventSystem.current?.SetSelectedGameObject(null);
        }

        void Update()
        {
            if (!IsOpen)
                return;
            if (partMode)
                stage.Tick(Time.unscaledDeltaTime, !turningPreview);
            if (Keyboard.current.escapeKey.wasPressedThisFrame && MenuState.ChangedFrame != Time.frameCount)
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
            OnPickerChanged();
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
            panelRect = panel;
            // The picker itself keeps to the right-hand 860 px; the part's colours (part mode) go on the left.
            var picker = UiFactory.Rect("Picker", panel);
            picker.Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(860f, 740f));

            var stripes = UiFactory.Hazard(panel, "Stripes", theme);
            stripes.rectTransform.anchorMin = new Vector2(0f, 1f);
            stripes.rectTransform.anchorMax = new Vector2(1f, 1f);
            stripes.rectTransform.offsetMin = new Vector2(14f, -24f);
            stripes.rectTransform.offsetMax = new Vector2(-14f, -12f);
            pickerTitle = UiFactory.Text(picker, "Title", theme, "PAINT COLOR", 44f, theme.text, true);
            var title = pickerTitle;
            title.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -30f), new Vector2(500f, 56f));

            // Saturation / brightness square.
            squareTexture = new Texture2D(SquareResolution, SquareResolution, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var squareRect = UiFactory.Rect("Square", picker).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -104f), new Vector2(340f, 340f));
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
            var hueRect = UiFactory.Rect("Hue", picker).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(390f, -104f), new Vector2(40f, 340f));
            hueRect.gameObject.AddComponent<RawImage>().texture = hueTexture;
            DragArea.Add(hueRect, p => SetHsv(p.y, saturation, value));
            hueHandle = UiFactory.Image(hueRect, "Handle", Color.white, theme.roundedOutline).rectTransform;
            hueHandle.sizeDelta = new Vector2(52f, 14f);

            // Old / new swatches.
            var swatchLabel = UiFactory.Text(picker, "SwatchLabel", theme, "OLD  /  NEW", 18f, theme.textDim, true);
            swatchLabel.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(460f, -100f), new Vector2(360f, 24f));
            oldSwatch = UiFactory.Image(picker, "Old", Color.white, theme.rounded);
            oldSwatch.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(460f, -128f), new Vector2(175f, 56f));
            newSwatch = UiFactory.Image(picker, "New", Color.white, theme.rounded);
            newSwatch.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(643f, -128f), new Vector2(175f, 56f));

            // RGB sliders and hex.
            string[] names = { "R", "G", "B" };
            for (var i = 0; i < 3; i++)
            {
                var channel = i;
                var y = -206f - i * 52f;
                var label = UiFactory.Text(picker, names[i], theme, names[i], 24f, theme.text, true);
                label.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(460f, y - 6f), new Vector2(30f, 36f));
                var sliderRect = UiFactory.Rect(names[i] + "Slider", picker).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(490f, y), new Vector2(230f, 44f));
                rgb[i] = UiFactory.Slider(sliderRect, "Slider", theme, 0f, 255f, 0f, v =>
                {
                    var c = current;
                    c[channel] = Mathf.Round(v) / 255f;
                    SetColor(c, true);
                });
                ((RectTransform)rgb[i].transform).Fill();
                rgb[i].wholeNumbers = true;
                rgbFields[i] = UiFactory.InputField(picker, names[i] + "Field", theme, "", 22f);
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
            var hexLabel = UiFactory.Text(picker, "HexLabel", theme, "HEX", 24f, theme.text, true);
            hexLabel.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(460f, -368f), new Vector2(60f, 36f));
            hex = UiFactory.InputField(picker, "Hex", theme, "#RRGGBB", 24f);
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
            var presetLabel = UiFactory.Text(picker, "PresetLabel", theme, "TEAM COLORS", 18f, theme.textDim, true);
            presetLabel.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(460f, -432f), new Vector2(300f, 24f));
            for (var i = 0; i < Presets.Length; i++)
            {
                var preset = Presets[i];
                var swatch = UiFactory.Image(picker, "Preset" + i, preset, theme.rounded, true);
                swatch.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(460f + (i % 6) * 60f, -462f - (i / 6) * 56f), new Vector2(50f, 48f));
                var button = swatch.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => SetColor(preset, true));
            }

            // Recent colours: 5 × 4.
            var recentLabel = UiFactory.Text(picker, "RecentLabel", theme, "RECENT", 18f, theme.textDim, true);
            recentLabel.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -460f), new Vector2(200f, 24f));
            for (var i = 0; i < 20; i++)
            {
                var col = i % 5;
                var row = i / 5;
                var index = i;
                var swatch = UiFactory.Image(picker, "Recent" + i, Color.white, theme.rounded, true);
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

            var use = UiFactory.Button(panel, "Use", theme, "USE COLOR", () =>
            {
                if (partMode)
                    ApplyToPart();
                else
                    onChosen?.Invoke(current);
                Close();
            }, true, 26f);
            useLabel = use.GetComponentInChildren<TextMeshProUGUI>();
            ((RectTransform)use.transform).Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-32f, 32f), new Vector2(240f, 62f));
            var cancel = UiFactory.Button(panel, "Cancel", theme, "CANCEL", Close, false, 24f);
            ((RectTransform)cancel.transform).Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-288f, 32f), new Vector2(170f, 62f));
            BuildPartArea(panel);
        }

        // ---------------------------------------------------------------- part mode

        const int MaxPartColors = 8;

        RectTransform panelRect;
        TextMeshProUGUI pickerTitle;
        TextMeshProUGUI useLabel;
        GameObject partArea;
        TextMeshProUGUI partName;
        RawImage preview;
        readonly List<(Image back, Image chip, TextMeshProUGUI label)> partSwatches = new();
        bool partMode;
        bool selecting;
        bool turningPreview;
        HoverTarget part;
        List<Material> partMaterials = new();
        readonly Dictionary<Material, Color> working = new();
        Action<HoverTarget> partApplied;
        int selected;
        PartPreviewStage stage;

        /// <summary>
        /// Opens on a part: a turning preview of it and one swatch per material it's made of (e.g. a wheel's
        /// tyre and hub). Click a swatch to edit that colour with the picker; the preview shows it straight
        /// away. Apply paints the real part; cancel leaves it as it was.
        /// </summary>
        public void OpenForPart(HoverTarget target, IEnumerable<MeshRenderer> detail, IReadOnlyList<Color> recentColors, Action<HoverTarget> applied)
        {
            part = target;
            partApplied = applied;
            recent = recentColors;
            partMode = true;
            partMaterials = target.Materials;
            if (partMaterials.Count > MaxPartColors)
                partMaterials.RemoveRange(MaxPartColors, partMaterials.Count - MaxPartColors);
            working.Clear();
            foreach (var pair in target.Paints)
                working[pair.Key] = pair.Value;

            panelRect.sizeDelta = new Vector2(1300f, 740f);
            partArea.SetActive(true);
            partName.text = target.Title;
            pickerTitle.text = "EDIT COLOR";
            useLabel.text = "APPLY";
            for (var i = 0; i < partSwatches.Count; i++)
            {
                var used = i < partMaterials.Count;
                partSwatches[i].back.gameObject.SetActive(used);
                if (used)
                    partSwatches[i].label.text = NiceName(partMaterials[i].name);
            }

            stage ??= new PartPreviewStage(theme.card);
            stage.Show(target.gameObject, Parts.PartShape.DetailRenderers(target.gameObject));
            preview.texture = stage.Texture;
            stage.Paint(working);

            root.SetActive(true);
            MenuState.Open(this);
            RefreshRecent();
            Select(0);
        }

        void Select(int index)
        {
            selected = Mathf.Clamp(index, 0, partMaterials.Count - 1);
            var color = part.ColorOf(partMaterials[selected]);
            if (working.TryGetValue(partMaterials[selected], out var painted))
                color = painted;
            oldSwatch.color = color;
            selecting = true;
            SetColor(color, true);
            selecting = false;
            RefreshPartSwatches();
        }

        /// <summary>The picker changed colour: in part mode that's the selected material's new paint.</summary>
        void OnPickerChanged()
        {
            if (!partMode || selecting || partMaterials.Count == 0)
                return;
            working[partMaterials[selected]] = current;
            RefreshPartSwatches();
            stage.Paint(working);
        }

        void ResetSelected()
        {
            if (!partMode || partMaterials.Count == 0)
                return;
            working.Remove(partMaterials[selected]);
            stage.Paint(working);
            Select(selected);
        }

        void RefreshPartSwatches()
        {
            for (var i = 0; i < partMaterials.Count && i < partSwatches.Count; i++)
            {
                var material = partMaterials[i];
                partSwatches[i].chip.color = working.TryGetValue(material, out var c) ? c : part.ColorOf(material);
                partSwatches[i].back.color = i == selected ? theme.accent : theme.panelInset;
                partSwatches[i].label.color = i == selected ? theme.accentText : theme.text;
            }
        }

        void ApplyToPart()
        {
            if (part == null)
                return;
            foreach (var material in partMaterials)
                part.SetPaint(material, working.TryGetValue(material, out var c) ? c : null);
            partApplied?.Invoke(part);
        }

        void LeavePartMode()
        {
            if (!partMode)
                return;
            partMode = false;
            stage?.Hide();
            partArea.SetActive(false);
            panelRect.sizeDelta = new Vector2(860f, 740f);
            pickerTitle.text = "PAINT COLOR";
            useLabel.text = "USE COLOR";
            part = null;
        }

        /// <summary>"Rubber_-_Black_-_Version_1.1_1" → "Rubber - Black".</summary>
        static string NiceName(string material)
        {
            var name = material.Replace("_-_", " - ").Replace('_', ' ');
            var version = name.IndexOf(" - Version", StringComparison.Ordinal);
            if (version > 0)
                name = name.Substring(0, version);
            return name.Trim();
        }

        void BuildPartArea(RectTransform panel)
        {
            var area = UiFactory.Rect("PartArea", panel);
            area.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(440f, 740f));
            partArea = area.gameObject;

            var title = UiFactory.Text(area, "Title", theme, "PART COLORS", 44f, theme.text, true);
            title.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -30f), new Vector2(400f, 56f));
            partName = UiFactory.Text(area, "Part", theme, "", 22f, theme.textDim);
            partName.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -84f), new Vector2(400f, 28f));

            var frame = UiFactory.Image(area, "PreviewFrame", theme.card, theme.rounded);
            frame.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -116f), new Vector2(392f, 326f));
            var previewRect = UiFactory.Rect("Preview", frame.transform).Fill(4f, 4f, 4f, 4f);
            preview = previewRect.gameObject.AddComponent<RawImage>();
            preview.raycastTarget = true;
            var turn = previewRect.gameObject.AddComponent<DragTurn>();
            turn.turned = d => stage?.Turn(d * 0.4f);
            turn.dragging = d => turningPreview = d;
            var hint = UiFactory.Text(frame.transform, "Hint", theme, "Drag to turn", 16f, new Color(theme.cardText.r, theme.cardText.g, theme.cardText.b, 0.6f), false, TextAlignmentOptions.BottomRight);
            hint.rectTransform.Fill(8f, 6f, 10f, 6f);

            var label = UiFactory.Text(area, "ColorsLabel", theme, "COLORS  (click one to change it)", 18f, theme.textDim, true);
            label.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -454f), new Vector2(400f, 24f));
            for (var i = 0; i < MaxPartColors; i++)
            {
                var index = i;
                var back = UiFactory.Image(area, "Color" + i, theme.panelInset, theme.rounded, true);
                back.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f + (i % 2) * 198f, -482f - (i / 2) * 50f), new Vector2(192f, 44f));
                var button = back.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() =>
                {
                    Select(index);
                    Audio.Sounds.Play(Audio.Sounds.Tick, 0.25f);
                });
                var chip = UiFactory.Image(back.transform, "Chip", Color.white, theme.rounded);
                chip.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(32f, 32f));
                var text = UiFactory.Text(back.transform, "Name", theme, "", 18f, theme.text, false, TextAlignmentOptions.MidlineLeft);
                text.rectTransform.Fill(46f, 0f, 6f, 0f);
                partSwatches.Add((back, chip, text));
            }
            var reset = UiFactory.Button(area, "Reset", theme, "ORIGINAL COLOR", ResetSelected, false, 20f);
            ((RectTransform)reset.transform).Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(32f, 32f), new Vector2(220f, 52f));
            partArea.SetActive(false);
        }
    }

    /// <summary>Reports how far the pointer is dragged each frame (pixels), and when a drag starts and stops.</summary>
    public class DragTurn : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Action<Vector2> turned;
        public Action<bool> dragging;

        public void OnBeginDrag(PointerEventData e) => dragging?.Invoke(true);
        public void OnDrag(PointerEventData e) => turned?.Invoke(e.delta);
        public void OnEndDrag(PointerEventData e) => dragging?.Invoke(false);
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
