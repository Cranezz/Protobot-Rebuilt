using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Interface {
    // V2: a dropdown: a button showing the chosen item; clicking it opens a list under it (scrolls when long).
    // Clicking outside or Escape closes it.
    public class Popup : MonoBehaviour {
        private static Popup open;
        private RectTransform list;

        public static Button Choice(Transform parent, string[] labels, int selected, Action<int> onSelect, float width = 110f) {
            var button = Ui.Button(parent, "Choice", null, null);
            button.Size(width, 32f);
            button.targetGraphic.color = Theme.Field;
            var label = Ui.Text(button.transform, "Label", selected >= 0 && selected < labels.Length ? labels[selected] : "",
                Theme.FontBody, Theme.Text, Theme.Regular);
            label.rectTransform.Fill(10, 0, 24, 0);
            Chevron(button.transform);
            button.onClick.AddListener(() => Open((RectTransform)button.transform, labels, selected, i => {
                label.text = labels[i];
                onSelect(i);
            }));
            return button;
        }

        /// <summary>A small down chevron at the right of a button.</summary>
        public static void Chevron(Transform button) {
            var chevron = Ui.Image(button, "Chevron", Theme.TextDim);
            chevron.sprite = Interface.Instance != null ? Interface.Instance.chevronIcon : null;
            chevron.preserveAspect = true;
            chevron.rectTransform.Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(10f, 10f));
            chevron.rectTransform.localEulerAngles = new Vector3(0f, 0f, 180f);  // the icon points up
        }

        public static void Open(RectTransform anchor, string[] labels, int selected, Action<int> onSelect) {
            Close();
            var root = Interface.Root;
            var go = Ui.Rect("Popup", root).Fill().gameObject;
            open = go.AddComponent<Popup>();
            const float itemHeight = 28f;
            var height = Mathf.Min(labels.Length * itemHeight + 8f, 360f);

            var corners = new Vector3[4];
            anchor.GetWorldCorners(corners);
            var bottomLeft = Layout.FromScreen(corners[0]);
            var panel = Ui.Panel(go.transform, "Panel", Theme.PanelRaised, 6f).rectTransform;
            panel.Place(Vector2.zero, new Vector2(0f, 1f), bottomLeft + new Vector2(0f, -4f),
                new Vector2(Mathf.Max(anchor.rect.width, 120f), height));
            open.list = panel;

            var viewport = Ui.Rect("Viewport", panel).Fill(4, 4, 4, 4);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Ui.Rect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, labels.Length * itemHeight);
            var scroll = panel.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            for (var i = 0; i < labels.Length; i++) {
                var index = i;
                var item = Ui.Image(content, labels[i], i == selected ? Theme.AccentSoft : new Color(1f, 1f, 1f, 0f), 4f, true);
                item.rectTransform.anchorMin = new Vector2(0f, 1f);
                item.rectTransform.anchorMax = new Vector2(1f, 1f);
                item.rectTransform.pivot = new Vector2(0.5f, 1f);
                item.rectTransform.anchoredPosition = new Vector2(0f, -i * itemHeight);
                item.rectTransform.sizeDelta = new Vector2(0f, itemHeight - 2f);
                var hover = item.gameObject.AddComponent<HoverTint>();
                hover.target = item;
                var text = Ui.Text(item.transform, "Text", labels[i], Theme.FontBody, i == selected ? Theme.Text : Theme.TextDim);
                text.rectTransform.Fill(10, 0, 10, 0);
                var button = item.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => {
                    Close();
                    onSelect(index);
                });
            }
            // Show the chosen item.
            if (selected > 0)
                content.anchoredPosition = new Vector2(0f, Mathf.Clamp(selected * itemHeight - height / 2f, 0f, Mathf.Max(0f, labels.Length * itemHeight - height + 8f)));
        }

        public static void Close() {
            if (open != null)
                Destroy(open.gameObject);
            open = null;
        }

        public static bool IsOpen => open != null;

        private void Update() {
            var mouse = Mouse.current;
            var outside = mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
                          && !RectTransformUtility.RectangleContainsScreenPoint(list, mouse.position.ReadValue());
            if (outside || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame))
                Close();
        }
    }
}
