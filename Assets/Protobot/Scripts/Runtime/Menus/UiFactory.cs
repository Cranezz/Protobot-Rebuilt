using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Protobot.Menus
{
    /// <summary>
    /// Builds themed uGUI elements from code, so every menu shares one look (see <see cref="UiTheme"/>).
    /// uGUI is used (not UI Toolkit) because the same canvases can later be placed in the world for VR.
    /// </summary>
    public static class UiFactory
    {
        public static Canvas Canvas(GameObject host, int sortingOrder)
        {
            var canvas = host.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = host.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            host.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Stretches to the parent with the given insets (left, bottom, right, top).</summary>
        public static RectTransform Fill(this RectTransform rt, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>Fixed-size box anchored at a point of the parent (0..1), offset in pixels.</summary>
        public static RectTransform Place(this RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image Image(Transform parent, string name, Color color, Sprite sprite = null, bool raycast = false)
        {
            var rt = Rect(name, parent);
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null && sprite.border != Vector4.zero ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        static Sprite circle;

        /// <summary>A smooth-edged filled circle (made once at runtime), for round buttons and discs.</summary>
        public static Sprite Circle()
        {
            if (circle != null)
                return circle;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Circle", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f));
                    var alpha = Mathf.Clamp01(size / 2f - d);  // one pixel of anti-aliasing at the edge
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            circle = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            return circle;
        }

        static Sprite downArrow;

        /// <summary>A small filled triangle pointing down (drop-down carets); made once at runtime.</summary>
        public static Sprite DownArrow()
        {
            if (downArrow != null)
                return downArrow;
            const int w = 32, h = 20;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Down arrow", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[w * h];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    // Point at the bottom middle; the triangle widens towards the top.
                    var halfWidth = (y + 0.5f) / h * (w / 2f);
                    var alpha = Mathf.Clamp01(halfWidth - Mathf.Abs(x + 0.5f - w / 2f) + 0.5f);
                    pixels[y * w + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            downArrow = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f);
            return downArrow;
        }

        public static Image Panel(Transform parent, string name, UiTheme theme, Color color, bool shadow = false)
        {
            if (shadow)
            {
                var s = Image(parent, name + "_Shadow", new Color(0f, 0f, 0f, 0.6f), theme.shadow);
                s.rectTransform.Fill(-36f, -44f, -36f, -28f);
            }
            var image = Image(parent, name, color, theme.rounded, true);
            return image;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, UiTheme theme, string text, float size, Color color,
            bool bold = false, TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var rt = Rect(name, parent);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.font = bold && theme.boldFont != null ? theme.boldFont : theme.font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            // Overflow, not Ellipsis: with Ellipsis a line taller than its box (font line height > box) disappears entirely.
            tmp.overflowMode = TextOverflowModes.Overflow;
            return tmp;
        }

        public static Button Button(Transform parent, string name, UiTheme theme, string label, UnityAction onClick,
            bool accent = false, float fontSize = 24f)
        {
            var image = Image(parent, name, Color.white, theme.rounded, true);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = accent ? theme.accent : theme.button;
            colors.highlightedColor = accent ? Color.Lerp(theme.accent, Color.white, 0.25f) : theme.buttonHover;
            colors.selectedColor = colors.normalColor;
            colors.pressedColor = accent ? Color.Lerp(theme.accent, Color.black, 0.2f) : Color.Lerp(theme.buttonHover, Color.white, 0.15f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            var nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;
            if (onClick != null)
                button.onClick.AddListener(onClick);
            button.onClick.AddListener(() => Audio.Sounds.Play(Audio.Sounds.Tick, 0.25f));

            var text = Text(image.transform, "Label", theme, label, fontSize, accent ? theme.accentText : theme.text, true, TextAlignmentOptions.Center);
            text.rectTransform.Fill(8f, 0f, 8f, 0f);
            return button;
        }

        /// <summary>Thin strip of yellow/black safety stripes.</summary>
        public static Image Hazard(Transform parent, string name, UiTheme theme)
        {
            var image = Image(parent, name, Color.white, theme.hazardStripes);
            image.type = UnityEngine.UI.Image.Type.Tiled;
            image.pixelsPerUnitMultiplier = 2f;
            return image;
        }

        public static TMP_InputField InputField(Transform parent, string name, UiTheme theme, string placeholder, float fontSize = 24f)
        {
            var bg = Image(parent, name, theme.panelInset, theme.rounded, true);
            var field = bg.gameObject.AddComponent<TMP_InputField>();
            var area = Rect("TextArea", bg.transform).Fill(16f, 4f, 16f, 4f);
            area.gameObject.AddComponent<RectMask2D>();
            var hint = Text(area, "Placeholder", theme, placeholder, fontSize, theme.textDim, false, TextAlignmentOptions.MidlineLeft);
            hint.rectTransform.Fill();
            hint.fontStyle = FontStyles.Italic;
            var value = Text(area, "Text", theme, "", fontSize, theme.text, false, TextAlignmentOptions.MidlineLeft);
            value.rectTransform.Fill();
            field.textViewport = area;
            field.textComponent = value;
            field.placeholder = hint;
            field.fontAsset = theme.font;
            field.pointSize = fontSize;
            field.caretColor = theme.accent;
            field.selectionColor = new Color(theme.accent.r, theme.accent.g, theme.accent.b, 0.35f);
            field.customCaretColor = true;
            return field;
        }

        public static Slider Slider(Transform parent, string name, UiTheme theme, float min, float max, float value, UnityAction<float> onChange)
        {
            var root = Rect(name, parent);
            var slider = root.gameObject.AddComponent<Slider>();
            var track = Image(root, "Track", theme.panelInset, theme.rounded, true);
            track.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            track.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            track.rectTransform.sizeDelta = new Vector2(0f, 10f);
            var fillArea = Rect("FillArea", root);
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.sizeDelta = new Vector2(0f, 10f);
            var fill = Image(fillArea, "Fill", theme.accent, theme.rounded);
            fill.rectTransform.sizeDelta = Vector2.zero;
            // The slider stretches the handle over the handle area's height, so that area is a zero-height
            // line through the middle and the handle's own size is the knob.
            var handleArea = Rect("HandleArea", root);
            handleArea.anchorMin = new Vector2(0f, 0.5f);
            handleArea.anchorMax = new Vector2(1f, 0.5f);
            handleArea.sizeDelta = new Vector2(-22f, 0f);
            var handle = Image(handleArea, "Handle", theme.text, theme.rounded, true);
            handle.rectTransform.sizeDelta = new Vector2(22f, 22f);
            handle.pixelsPerUnitMultiplier = 2f;  // corners as round as the knob is small
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.minValue = min;
            slider.maxValue = max;
            slider.SetValueWithoutNotify(value);
            var nav = slider.navigation;
            nav.mode = Navigation.Mode.None;
            slider.navigation = nav;
            slider.onValueChanged.AddListener(onChange);
            return slider;
        }

        /// <summary>A settings row: label on the left, control area on the right (returned).</summary>
        public static RectTransform Row(Transform parent, string label, UiTheme theme, float height = 52f)
        {
            var row = Rect(label + "Row", parent);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            var text = Text(row, "Label", theme, label, 24f, theme.text, false, TextAlignmentOptions.MidlineLeft);
            text.rectTransform.anchorMin = new Vector2(0f, 0f);
            text.rectTransform.anchorMax = new Vector2(0.45f, 1f);
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            var control = Rect("Control", row);
            control.anchorMin = new Vector2(0.45f, 0.12f);
            control.anchorMax = new Vector2(1f, 0.88f);
            control.offsetMin = control.offsetMax = Vector2.zero;
            return control;
        }

        public static void VerticalList(RectTransform rt, float spacing, RectOffset padding = null)
        {
            var layout = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding ?? new RectOffset();
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }
    }

    /// <summary>"&lt; value &gt;" picker for a fixed list of options.</summary>
    public class OptionSelector : MonoBehaviour
    {
        TextMeshProUGUI valueText;
        List<string> options;
        Action<int> onChange;
        public int Index { get; private set; }

        public static OptionSelector Create(RectTransform area, UiTheme theme, IList<string> options, int index, Action<int> onChange)
        {
            var selector = area.gameObject.AddComponent<OptionSelector>();
            selector.options = new List<string>(options);
            selector.onChange = onChange;
            var bg = UiFactory.Image(area, "Background", theme.panelInset, theme.rounded);
            bg.rectTransform.Fill();
            var prev = UiFactory.Button(area, "Prev", theme, "<", () => selector.Step(-1), false, 26f);
            ((RectTransform)prev.transform).Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(48f, 0f));
            ((RectTransform)prev.transform).anchorMin = new Vector2(0f, 0f);
            ((RectTransform)prev.transform).anchorMax = new Vector2(0f, 1f);
            var next = UiFactory.Button(area, "Next", theme, ">", () => selector.Step(1), false, 26f);
            ((RectTransform)next.transform).anchorMin = new Vector2(1f, 0f);
            ((RectTransform)next.transform).anchorMax = new Vector2(1f, 1f);
            ((RectTransform)next.transform).pivot = new Vector2(1f, 0.5f);
            ((RectTransform)next.transform).sizeDelta = new Vector2(48f, 0f);
            ((RectTransform)next.transform).anchoredPosition = Vector2.zero;
            selector.valueText = UiFactory.Text(area, "Value", theme, "", 24f, theme.text, true, TextAlignmentOptions.Center);
            selector.valueText.rectTransform.Fill(52f, 0f, 52f, 0f);
            selector.SetIndex(index, false);
            return selector;
        }

        public void SetIndex(int index, bool notify)
        {
            Index = Mathf.Clamp(index, 0, options.Count - 1);
            valueText.text = options[Index];
            if (notify)
                onChange?.Invoke(Index);
        }

        /// <summary>Shows a value that isn't one of the options (e.g. "Custom") without changing the index.</summary>
        public void ShowText(string text) => valueText.text = text;

        void Step(int direction) => SetIndex((Index + direction + options.Count) % options.Count, true);
    }

    /// <summary>
    /// A drop-down list: shows the chosen option; click it to open the list below, click an option to pick
    /// it (clicking anywhere else closes the list). The list draws above everything else in its menu.
    /// </summary>
    public class DropdownMenu : MonoBehaviour
    {
        TextMeshProUGUI valueText;
        List<string> options;
        Action<int> onChange;
        GameObject list;
        readonly List<Image> optionBacks = new();
        UiTheme theme;
        public int Index { get; private set; }

        public static DropdownMenu Create(RectTransform area, UiTheme theme, IList<string> options, int index, Action<int> onChange)
        {
            var dropdown = area.gameObject.AddComponent<DropdownMenu>();
            dropdown.theme = theme;
            dropdown.options = new List<string>(options);
            dropdown.onChange = onChange;

            var button = UiFactory.Button(area, "Current", theme, "", dropdown.Toggle, false, 22f);
            ((RectTransform)button.transform).Fill();
            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.rectTransform.Fill(16f, 0f, 40f, 0f);
            dropdown.valueText = label;
            var caret = UiFactory.Image(button.transform, "Caret", theme.textDim, UiFactory.DownArrow());
            caret.rectTransform.Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(14f, 9f));

            // The open list: its own canvas so it draws over the rows below it and still gets clicks.
            var listRect = UiFactory.Rect("List", area);
            listRect.anchorMin = new Vector2(0f, 0f);
            listRect.anchorMax = new Vector2(1f, 0f);
            listRect.pivot = new Vector2(0.5f, 1f);
            listRect.anchoredPosition = new Vector2(0f, -4f);
            const float rowHeight = 40f;
            listRect.sizeDelta = new Vector2(0f, options.Count * rowHeight + 8f);
            var canvas = listRect.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 40;
            listRect.gameObject.AddComponent<GraphicRaycaster>();
            var back = UiFactory.Image(listRect, "Back", theme.panelInset, theme.rounded, true);
            back.rectTransform.Fill();
            for (var i = 0; i < options.Count; i++)
            {
                var choice = i;
                var row = UiFactory.Image(listRect, "Option" + i, new Color(1f, 1f, 1f, 0f), theme.rounded, true);
                row.rectTransform.anchorMin = new Vector2(0f, 1f);
                row.rectTransform.anchorMax = new Vector2(1f, 1f);
                row.rectTransform.pivot = new Vector2(0.5f, 1f);
                row.rectTransform.sizeDelta = new Vector2(-8f, rowHeight);
                row.rectTransform.anchoredPosition = new Vector2(0f, -4f - i * rowHeight);
                var rowButton = row.gameObject.AddComponent<Button>();
                rowButton.transition = Selectable.Transition.None;
                rowButton.onClick.AddListener(() =>
                {
                    dropdown.SetIndex(choice, true);
                    dropdown.Close();
                });
                row.gameObject.AddComponent<HoverTint>().Setup(row, new Color(1f, 1f, 1f, 0.08f));
                var text = UiFactory.Text(row.transform, "Label", theme, options[i], 22f, theme.text, false, TextAlignmentOptions.MidlineLeft);
                text.rectTransform.Fill(14f, 0f, 8f, 0f);
                dropdown.optionBacks.Add(row);
            }
            dropdown.list = listRect.gameObject;
            dropdown.list.SetActive(false);
            dropdown.SetIndex(index, false);
            return dropdown;
        }

        public bool IsOpen => list != null && list.activeSelf;

        public void SetIndex(int index, bool notify)
        {
            Index = Mathf.Clamp(index, 0, options.Count - 1);
            valueText.text = options[Index];
            for (var i = 0; i < optionBacks.Count; i++)
                optionBacks[i].GetComponentInChildren<TextMeshProUGUI>().color = i == Index ? theme.accent : theme.text;
            if (notify)
                onChange?.Invoke(Index);
        }

        void Toggle() => list.SetActive(!list.activeSelf);

        public void Close() => list.SetActive(false);

        void Update()
        {
            // A click outside the open list closes it.
            if (!IsOpen || UnityEngine.InputSystem.Mouse.current == null || !UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame)
                return;
            var pointer = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
            if (!RectTransformUtility.RectangleContainsScreenPoint((RectTransform)list.transform, pointer)
                && !RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform, pointer))
                Close();
        }
    }

    /// <summary>Lightens a graphic while the pointer is over it.</summary>
    public class HoverTint : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        Graphic graphic;
        Color normal;
        Color hover;

        public void Setup(Graphic target, Color hoverColor)
        {
            graphic = target;
            normal = target.color;
            hover = hoverColor;
        }

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) => graphic.color = hover;
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) => graphic.color = normal;
    }

    /// <summary>On/off switch drawn as a small pill.</summary>
    public class ToggleSwitch : MonoBehaviour
    {
        Image track;
        RectTransform knob;
        UiTheme theme;
        Action<bool> onChange;
        public bool On { get; private set; }

        public static ToggleSwitch Create(RectTransform area, UiTheme theme, bool on, Action<bool> onChange)
        {
            var root = UiFactory.Rect("Switch", area).Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(84f, 38f));
            var toggle = root.gameObject.AddComponent<ToggleSwitch>();
            toggle.theme = theme;
            toggle.onChange = onChange;
            toggle.track = UiFactory.Image(root, "Track", theme.panelInset, theme.rounded, true);
            toggle.track.rectTransform.Fill();
            var button = toggle.track.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => toggle.Set(!toggle.On, true));
            toggle.knob = UiFactory.Image(root, "Knob", theme.text, theme.rounded).rectTransform;
            toggle.knob.sizeDelta = new Vector2(34f, 30f);
            toggle.Set(on, false);
            return toggle;
        }

        public void Set(bool on, bool notify)
        {
            On = on;
            track.color = on ? theme.accent : theme.panelInset;
            knob.anchorMin = knob.anchorMax = new Vector2(on ? 1f : 0f, 0.5f);
            knob.pivot = new Vector2(on ? 1f : 0f, 0.5f);
            knob.anchoredPosition = new Vector2(on ? -4f : 4f, 0f);
            if (notify)
                onChange?.Invoke(on);
        }
    }
}
