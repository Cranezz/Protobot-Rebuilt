using System.Linq;
using System;
using Protobot.InputEvents;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Interface {
    // V2: a window over the darkened app (importing a part, the plastic sheets): a title with a close button, a
    // body the caller fills, and a footer for its buttons. Esc closes it (when no text box is being typed in).
    public class Dialog : MonoBehaviour {
        public RectTransform Panel { get; private set; }
        public RectTransform Body { get; private set; }
        public RectTransform Footer { get; private set; }
        public TextMeshProUGUI Title { get; private set; }
        public Action OnClose;
        public bool closeOnEscape = true;

        /// <summary>How many dialogs are open (tools and shortcuts in the 3D view wait while one is).</summary>
        public static int OpenCount { get; private set; }

        public static Dialog Open(string title, Vector2 size) {
            var root = Ui.Rect("Dialog " + title, Interface.Root).Fill();
            root.SetAsLastSibling();
            var backdrop = root.gameObject.AddComponent<Image>();
            backdrop.color = Theme.Backdrop;
            backdrop.raycastTarget = true;
            var dialog = root.gameObject.AddComponent<Dialog>();

            var panel = Ui.Panel(root, "Panel", Theme.Panel, 12f).rectTransform;
            panel.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            dialog.Panel = panel;

            var header = Ui.Rect("Header", panel).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -14f), new Vector2(size.x - 40f, 32f));
            dialog.Title = Ui.Text(header, "Title", title, Theme.FontTitle + 2f, Theme.Text, Theme.Medium);
            dialog.Title.rectTransform.Fill(0, 0, 40, 0);
            var close = Ui.Button(header, "Close", "×", dialog.Close, false, 6f, 22f);
            ((RectTransform)close.transform).Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(32f, 32f));
            close.targetGraphic.color = new Color(1f, 1f, 1f, 0f);
            Ui.Tip(close, "Close", "Esc");

            dialog.Body = Ui.Rect("Body", panel).Fill(20, 58, 20, 68);
            dialog.Footer = Ui.Rect("Footer", panel);
            dialog.Footer.anchorMin = new Vector2(0f, 0f);
            dialog.Footer.anchorMax = new Vector2(1f, 0f);
            dialog.Footer.pivot = new Vector2(0.5f, 0f);
            dialog.Footer.anchoredPosition = new Vector2(0f, 16f);
            dialog.Footer.sizeDelta = new Vector2(-40f, 36f);
            Ui.Row(dialog.Footer, 10f, null, TextAnchor.MiddleRight);
            OverHomeMenu(root);
            OpenCount++;
            return dialog;
        }

        // V2: a window opened from the home menu (Report a bug) was drawn under it: the home menu's canvas is above the
        // interface's. While it's showing, the window gets a canvas of its own just above it.
        private static void OverHomeMenu(RectTransform root) {
            var launch = GameObject.Find("Launch Canvas");
            if (launch == null || !launch.TryGetComponent(out Canvas home) || !home.enabled) return;
            if (!launch.GetComponentsInChildren<Transform>().Any(t => t.name == "Splash Screen")) return;   // not open
            var canvas = root.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = home.sortingOrder + 1;
            root.gameObject.AddComponent<GraphicRaycaster>();
        }

        public void Close() {
            if (this == null) return;
            Destroy(gameObject);
        }

        /// <summary>Closes every open window (an online match starting: it needs the keys and the view).</summary>
        public static void CloseAll() {
            foreach (var dialog in FindObjectsByType<Dialog>(FindObjectsSortMode.None)) dialog.Close();
        }

        private void OnDestroy() {
            OpenCount = Mathf.Max(0, OpenCount - 1);
            OnClose?.Invoke();
        }

        private void Update() {
            if (closeOnEscape && transform.GetSiblingIndex() == transform.parent.childCount - 1 && !TextInput.IsTyping
                && !Popup.IsOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                Close();
        }

        /// <summary>A body row: a label on the left (fixed width), then the caller's widgets.</summary>
        public static RectTransform Row(RectTransform parent, string label, float height = 34f, float labelWidth = 104f) {
            var row = Ui.Rect(label, parent);
            Ui.Row(row, 8f);
            row.Size(-1, height);
            if (!string.IsNullOrEmpty(label)) {
                var text = Ui.Text(row, "Label", label, Theme.FontBody, Theme.TextDim);
                text.Size(labelWidth, height);
            }
            return row;
        }

        /// <summary>A section heading inside a body column.</summary>
        public static TextMeshProUGUI Heading(RectTransform parent, string text) {
            var heading = Ui.Text(parent, "Heading", text.ToUpperInvariant(), 12.5f, Theme.TextFaint, Theme.Medium);
            heading.characterSpacing = 6f;
            heading.Size(-1, 22f);
            return heading;
        }
    }
}
