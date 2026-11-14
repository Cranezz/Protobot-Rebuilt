using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Interface {
    // V2: a small hint by the mouse while it's over something a key acts on: the key in a key cap and a word or two
    // ("E  Place tubing" over a pneumatic fitting's port). Whoever wants it shown calls Show every frame; it goes
    // away the first frame nobody does. Doesn't take the mouse.
    public class KeyHint : MonoBehaviour {
        private static KeyHint instance;
        private RectTransform panel;
        private TextMeshProUGUI keyText, words;
        private int shownFrame = -1;
        private string key, text;

        public static void Create(Transform root) {
            var go = Ui.Rect("Key Hint", root).Fill().gameObject;
            instance = go.AddComponent<KeyHint>();
            instance.Build();
        }

        private void Build() {
            panel = Ui.Panel(transform, "Panel", Theme.PanelRaised, 5f).rectTransform;
            foreach (var g in panel.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
            panel.anchorMin = panel.anchorMax = Vector2.zero;
            panel.pivot = new Vector2(0f, 1f);
            panel.GetChild(0).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;   // the fill
            Ui.Row(panel, 6f, new RectOffset(5, 9, 4, 4));
            var fit = panel.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var cap = Ui.Key(panel, "E");
            keyText = cap.GetComponentInChildren<TextMeshProUGUI>();
            words = Ui.Text(panel, "Words", "", Theme.FontSmall, Theme.Text, Theme.Medium);
            words.overflowMode = TextOverflowModes.Overflow;
            panel.gameObject.SetActive(false);
        }

        /// <summary>Shows the hint by the mouse this frame.</summary>
        public static void Show(string key, string text) {
            if (instance == null) return;
            instance.shownFrame = Time.frameCount;
            instance.key = key;
            instance.text = text;
        }

        private void LateUpdate() {
            var on = shownFrame == Time.frameCount;
            if (!on) {
                if (panel.gameObject.activeSelf) panel.gameObject.SetActive(false);
                return;
            }
            if (keyText.text != key) keyText.text = key;
            if (words.text != text) words.text = text;
            if (!panel.gameObject.activeSelf) panel.gameObject.SetActive(true);
            transform.SetAsLastSibling();
            var canvas = (RectTransform)transform;
            var c = GetComponentInParent<Canvas>();
            var camera = c != null && c.renderMode != RenderMode.ScreenSpaceOverlay ? c.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, (Vector2)MouseInput.Position, camera, out var p);
            var rect = canvas.rect;
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            var size = panel.rect.size;
            // Below and to the right of the pointer, kept on the screen.
            var at = p - rect.min + new Vector2(16f, -18f);
            at.x = Mathf.Min(at.x, rect.width - size.x - 4f);
            at.y = Mathf.Max(at.y, size.y + 4f);
            panel.anchoredPosition = at;
        }
    }
}
