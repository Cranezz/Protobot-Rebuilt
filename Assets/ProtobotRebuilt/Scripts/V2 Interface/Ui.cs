using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Protobot.Interface {
    // V2: small factory for the new interface's widgets, all in the Theme's style.
    public static class Ui {
        /// <summary>The component, added if missing (GetComponent's "fake null" doesn't work with ??).</summary>
        public static T GetOrAdd<T>(this GameObject go) where T : Component =>
            go.TryGetComponent(out T existing) ? existing : go.AddComponent<T>();

        public static RectTransform Rect(string name, Transform parent) {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        public static RectTransform Place(this RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size) {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        public static RectTransform Fill(this RectTransform rect, float left = 0, float top = 0, float right = 0, float bottom = 0) {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        public static Image Image(Transform parent, string name, Color color, float radius = 0f, bool raycast = false) {
            var image = Rect(name, parent).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            if (radius > 0f) {
                image.sprite = Theme.Rounded;
                image.type = UnityEngine.UI.Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = Theme.RadiusMultiplier(radius);
            }
            return image;
        }

        /// <summary>A rounded panel with a faint border.</summary>
        public static Image Panel(Transform parent, string name, Color? color = null, float radius = Theme.Radius) {
            var border = Image(parent, name, Theme.Border, radius + 1f, true);
            var fill = Image(border.transform, "Fill", color ?? Theme.Panel, radius);
            fill.rectTransform.Fill(1, 1, 1, 1);
            return border;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, string text, float size = Theme.FontBody,
                                           Color? color = null, TMP_FontAsset font = null,
                                           TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft) {
            var label = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font != null ? font : Theme.Regular;
            label.fontSize = size;
            label.color = color ?? Theme.Text;
            label.alignment = align;
            label.text = text;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return label;
        }

        /// <summary>A flat button: background tints on hover/press; accent = filled blue.</summary>
        public static Button Button(Transform parent, string name, string label, Action onClick, bool accent = false,
                                    float radius = Theme.RadiusSmall, float fontSize = Theme.FontBody) {
            var background = Image(parent, name, accent ? Theme.Accent : Theme.PanelRaised, radius, true);
            var button = background.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = background;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = accent ? new Color(1.12f, 1.12f, 1.12f) : new Color(1.35f, 1.35f, 1.35f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.4f);
            colors.colorMultiplier = 1.5f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            if (!string.IsNullOrEmpty(label)) {
                var text = Text(background.transform, "Label", label, fontSize, accent ? Color.white : Theme.Text, Theme.Medium,
                    TextAlignmentOptions.Center);
                text.rectTransform.Fill(8, 0, 8, 0);
                text.overflowMode = TextOverflowModes.Overflow;
                // Wide enough for the label (measured by the layout, so it also works while hidden).
                var fit = background.gameObject.AddComponent<HorizontalLayoutGroup>();
                fit.padding = new RectOffset(10, 10, 0, 0);
                fit.childAlignment = TextAnchor.MiddleCenter;
                fit.childControlWidth = fit.childControlHeight = true;
                // Not "expand": that would make the button itself stretch in rows and columns.
                fit.childForceExpandWidth = fit.childForceExpandHeight = false;
            }
            if (onClick != null)
                button.onClick.AddListener(() => {
                    onClick();
                    EventSystem.current?.SetSelectedGameObject(null);
                });
            return button;
        }

        /// <summary>A square button with an icon.</summary>
        public static Button IconButton(Transform parent, string name, Sprite icon, Action onClick, float size = 32f,
                                        float iconSize = 18f, Color? background = null) {
            var button = Button(parent, name, null, onClick);
            button.targetGraphic.color = background ?? new Color(1f, 1f, 1f, 0f);
            ((RectTransform)button.transform).sizeDelta = new Vector2(size, size);
            var image = Image(button.transform, "Icon", Theme.Text);
            image.sprite = icon;
            image.preserveAspect = true;
            image.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(iconSize, iconSize));
            return button;
        }

        public static TMP_InputField Field(Transform parent, string name, string text, Action<string> onEnd,
                                           float fontSize = Theme.FontBody, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft) {
            var background = Image(parent, name, Theme.Field, Theme.RadiusSmall, true);
            var outline = background.gameObject.AddComponent<Outline>();
            outline.effectColor = Theme.Border;
            outline.effectDistance = new Vector2(1, -1);
            var area = Rect("Text Area", background.transform).Fill(8, 2, 8, 2);
            area.gameObject.AddComponent<RectMask2D>();
            var label = Text(area, "Text", text, fontSize, Theme.Text, Theme.Regular, align);
            label.rectTransform.Fill();
            label.overflowMode = TextOverflowModes.Overflow;
            var field = background.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = label;
            field.fontAsset = Theme.Regular;
            field.pointSize = fontSize;
            field.caretColor = Theme.Text;
            field.selectionColor = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.45f);
            field.customCaretColor = true;
            field.SetTextWithoutNotify(text);
            var navigation = field.navigation;
            navigation.mode = Navigation.Mode.None;
            field.navigation = navigation;
            field.onEndEdit.AddListener(_ => Protobot.InputEvents.TextInput.EditEndedFrame = Time.frameCount);
            if (onEnd != null)
                field.onEndEdit.AddListener(value => onEnd(value));
            return field;
        }

        /// <summary>A thin line used between groups.</summary>
        public static Image Divider(Transform parent, bool vertical = false) {
            var line = Image(parent, "Divider", Theme.Divider);
            var layout = line.gameObject.AddComponent<LayoutElement>();
            if (vertical) layout.preferredWidth = 1f;
            else layout.preferredHeight = 1f;
            return line;
        }

        public static HorizontalLayoutGroup Row(RectTransform rect, float spacing = 6f, RectOffset padding = null,
                                                TextAnchor align = TextAnchor.MiddleLeft) {
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding ?? new RectOffset();
            layout.childAlignment = align;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            return layout;
        }

        public static VerticalLayoutGroup Column(RectTransform rect, float spacing = 6f, RectOffset padding = null) {
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding ?? new RectOffset();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return layout;
        }

        public static T Size<T>(this T component, float width = -1f, float height = -1f, float flexibleWidth = -1f) where T : Component {
            var layout = component.gameObject.GetOrAdd<LayoutElement>();
            if (width >= 0f) layout.preferredWidth = layout.minWidth = width;
            if (height >= 0f) layout.preferredHeight = layout.minHeight = height;
            if (flexibleWidth >= 0f) layout.flexibleWidth = flexibleWidth;
            return component;
        }

        /// <summary>Hover tooltip for any element (title, optional shortcut and description).</summary>
        public static void Tip(Component target, string title, string shortcut = null, string description = null) {
            var tip = target.gameObject.GetOrAdd<TipSource>();
            tip.title = title;
            tip.shortcut = shortcut;
            tip.description = description;
        }

        /// <summary>A small rounded key cap ("R", "Esc") for hints.</summary>
        public static RectTransform Key(Transform parent, string key) {
            // The background is a separate child: a rounded Image would ask the layout for its sprite's width.
            var cap = Rect("Key " + key, parent);
            var background = Image(cap, "Background", Theme.Hover, 4f);
            background.rectTransform.Fill();
            background.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var layout = cap.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 1, 1);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            Text(cap, "Text", key, 12f, Theme.Text, Theme.Medium, TextAlignmentOptions.Center);
            var size = cap.gameObject.AddComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = 20f;
            size.minWidth = 20f;
            return cap;
        }

        /// <summary>A hint line: key caps and words, e.g. ("R", "rotate") pairs.</summary>
        public static RectTransform Hint(Transform parent, params string[] keyThenWords) {
            var row = Rect("Hint", parent);
            Row(row, 5f);
            for (var i = 0; i + 1 < keyThenWords.Length; i += 2) {
                Key(row, keyThenWords[i]);
                var words = Text(row, "Words", keyThenWords[i + 1], Theme.FontSmall, Theme.TextDim);
                words.margin = new Vector4(0, 0, 6, 0);
                words.Size(-1, 20f);
            }
            return row;
        }
    }

    // Hover tooltips: shows the Tooltip panel after a short delay.
    public class TipSource : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
        public string title, shortcut, description;

        public void OnPointerEnter(PointerEventData eventData) => Tooltip.Show(this);
        public void OnPointerExit(PointerEventData eventData) => Tooltip.Hide(this);
        private void OnDisable() => Tooltip.Hide(this);
    }
}
