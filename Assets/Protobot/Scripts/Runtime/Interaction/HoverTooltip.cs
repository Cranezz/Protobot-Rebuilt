using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Interaction
{
    /// <summary>
    /// Small label that follows the cursor: the hovered object's name and what you can do with it.
    /// Builds its own screen-space canvas, so it only needs adding to a GameObject.
    /// </summary>
    public class HoverTooltip : MonoBehaviour
    {
        static readonly Vector2 CursorOffset = new(22f, -18f);

        RectTransform panel;
        TextMeshProUGUI titleText;
        TextMeshProUGUI hintText;
        CanvasGroup group;
        Canvas canvas;

        void Awake() => Build();

        public void Show(string title, string hint, Vector2 screenPosition, bool available)
        {
            if (string.IsNullOrEmpty(title))
            {
                Hide();
                return;
            }
            titleText.text = title;
            hintText.text = hint;
            hintText.gameObject.SetActive(!string.IsNullOrEmpty(hint));
            hintText.color = available ? new Color(0.78f, 0.86f, 0.95f) : new Color(0.95f, 0.7f, 0.45f);
            group.alpha = 1f;

            // Keep the whole label on screen, flipping to the other side of the cursor near the edges.
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            var scale = canvas.scaleFactor;
            var size = panel.rect.size * scale;
            var pos = screenPosition + CursorOffset * scale;
            if (pos.x + size.x > Screen.width) pos.x = screenPosition.x - size.x - 8f * scale;
            if (pos.y - size.y < 0f) pos.y = screenPosition.y + size.y + 8f * scale;
            panel.position = pos;
        }

        public void Hide()
        {
            if (group != null)
                group.alpha = 0f;
        }

        void Build()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var panelGo = new GameObject("Tooltip", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(CanvasGroup));
            panel = (RectTransform)panelGo.transform;
            panel.SetParent(transform, false);
            panel.pivot = new Vector2(0f, 1f);
            panelGo.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.11f, 0.88f);
            panelGo.GetComponent<Image>().raycastTarget = false;
            var layout = panelGo.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 8, 8);
            layout.spacing = 2f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var fitter = panelGo.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            group = panelGo.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            titleText = MakeText("Title", 22f, FontStyles.Bold, Color.white);
            hintText = MakeText("Hint", 17f, FontStyles.Normal, Color.white);
            Hide();
        }

        TextMeshProUGUI MakeText(string name, float size, FontStyles style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(panel, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }
    }
}
