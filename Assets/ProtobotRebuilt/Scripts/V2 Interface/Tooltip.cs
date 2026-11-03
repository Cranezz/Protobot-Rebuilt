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
            // A click (opening a menu, choosing) puts the tip away until the mouse goes over something again.
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame))
                source = null;
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
            transform.SetAsLastSibling();  // over panels built after it

            // Below the element when it's in the top bar, else beside it (to its right, or its left when there's
            // no room); kept on the screen.
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            var target = (RectTransform)source.transform;
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            var canvas = (RectTransform)transform.parent;
            var camera = GetComponentInParent<Canvas>().renderMode == RenderMode.ScreenSpaceOverlay ? null : GetComponentInParent<Canvas>().worldCamera;
            Vector2 Local(Vector3 world) {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, RectTransformUtility.WorldToScreenPoint(camera, world), camera, out var p);
                return p;
            }
            var right = Local((corners[2] + corners[3]) / 2f);
            var left = Local((corners[0] + corners[1]) / 2f);
            var bottom = Local((corners[0] + corners[3]) / 2f);
            var size = panel.rect.size;
            var canvasRect = canvas.rect;
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            Vector2 center;
            if (bottom.y > canvasRect.yMax - 80f)
                center = bottom + new Vector2(0f, -10f - size.y / 2f);
            else if (right.x + 12f + size.x > canvasRect.xMax)
                center = left + new Vector2(-12f - size.x / 2f, 0f);
            else
                center = right + new Vector2(12f + size.x / 2f, 0f);
            center.x = Mathf.Clamp(center.x, canvasRect.xMin + size.x / 2f + 4f, canvasRect.xMax - size.x / 2f - 4f);
            center.y = Mathf.Clamp(center.y, canvasRect.yMin + size.y / 2f + 4f, canvasRect.yMax - size.y / 2f - 4f);
            panel.anchoredPosition = center;
        }
    }
}
