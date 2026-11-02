using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Interface {
    // V2: one tooltip for the new interface: a title, its shortcut in a key cap and a line of help, next to the
    // element under the mouse after a short delay.
    public class Tooltip : MonoBehaviour {
        private static Tooltip instance;
        private const float Delay = 0.35f;

        private RectTransform panel;
        private TextMeshProUGUI title, description;
        private RectTransform keyHolder;
        private TipSource source;
        private float shownAt;

        public static void Create(Transform root) {
            var go = Ui.Rect("Tooltip", root).Fill().gameObject;
            instance = go.AddComponent<Tooltip>();
            instance.Build();
        }

        private void Build() {
            panel = Ui.Panel(transform, "Panel", Theme.PanelRaised, 6f).rectTransform;
            panel.Place(Vector2.zero, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(240f, 60f));
            panel.GetChild(0).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;  // the fill
            Ui.Column(panel, 3f, new RectOffset(10, 10, 8, 8));
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var header = Ui.Rect("Header", panel);
            Ui.Row(header, 8f);
            header.Size(-1, 20f);
            title = Ui.Text(header, "Title", "", Theme.FontBody, Theme.Text, Theme.Medium);
            title.Size(-1, 20f, 1f);
            keyHolder = Ui.Rect("Keys", header);
            Ui.Row(keyHolder, 4f);
            description = Ui.Text(panel, "Description", "", Theme.FontSmall, Theme.TextDim);
            description.textWrappingMode = TextWrappingModes.Normal;
            description.overflowMode = TextOverflowModes.Overflow;
            panel.gameObject.SetActive(false);
        }

        public static void Show(TipSource tip) {
            if (instance == null)
                return;
            instance.source = tip;
            instance.shownAt = Time.unscaledTime + Delay;
        }

        public static void Hide(TipSource tip) {
            if (instance == null || instance.source != tip)
                return;
            instance.source = null;
            instance.panel.gameObject.SetActive(false);
        }

        private void LateUpdate() {
            if (source == null || !source.isActiveAndEnabled) {
                if (panel.gameObject.activeSelf)
                    panel.gameObject.SetActive(false);
                source = null;
                return;
            }
            if (Time.unscaledTime < shownAt || panel.gameObject.activeSelf)
                return;

            title.text = source.title;
            foreach (Transform child in keyHolder)
                Destroy(child.gameObject);
            if (!string.IsNullOrEmpty(source.shortcut))
                Ui.Key(keyHolder, source.shortcut);
            description.text = source.description ?? "";
            description.gameObject.SetActive(!string.IsNullOrEmpty(source.description));
            panel.gameObject.SetActive(true);

            // Beside the element: to its right, or to its left when there's no room.
            var target = (RectTransform)source.transform;
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            var canvas = (RectTransform)transform.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, (corners[2] + corners[3]) / 2f, null, out var right);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, (corners[0] + corners[1]) / 2f, null, out var left);
            var width = panel.sizeDelta.x;
            var canvasRect = canvas.rect;
            var useLeft = right.x + 12f + width > canvasRect.xMax;
            panel.pivot = new Vector2(useLeft ? 1f : 0f, 0.5f);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = useLeft ? left + new Vector2(-12f, 0f) : right + new Vector2(12f, 0f);
        }
    }
}
