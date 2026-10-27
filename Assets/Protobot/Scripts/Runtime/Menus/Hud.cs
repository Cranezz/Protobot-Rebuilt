using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Menus
{
    /// <summary>
    /// In-game overlay: the reticle in the centre of the screen (a dot, or a hand icon over things you can
    /// use) and a small label beside it naming what you're looking at and what clicking does.
    /// </summary>
    public class Hud : MonoBehaviour
    {
        [SerializeField] UiTheme theme;

        Image reticle;
        RectTransform tooltip;
        CanvasGroup tooltipGroup;
        TextMeshProUGUI titleText;
        TextMeshProUGUI hintText;
        Image toast;
        TextMeshProUGUI toastText;
        float toastUntil;

        void Awake()
        {
            UiFactory.Canvas(gameObject, 10);

            reticle = UiFactory.Image(transform, "Reticle", Color.white, theme.reticleDot);
            reticle.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24f, 24f));
            reticle.preserveAspect = true;

            tooltip = UiFactory.Image(transform, "Tooltip", new Color(theme.panel.r, theme.panel.g, theme.panel.b, 0.88f), theme.rounded).rectTransform;
            tooltip.Place(new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), new Vector2(34f, -26f), new Vector2(10f, 10f));
            var layout = tooltip.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 8, 10);
            layout.spacing = 0f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var fitter = tooltip.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            tooltipGroup = tooltip.gameObject.AddComponent<CanvasGroup>();
            tooltipGroup.blocksRaycasts = false;
            var stripe = UiFactory.Image(tooltip, "Accent", theme.accent);
            stripe.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            stripe.rectTransform.anchorMin = new Vector2(0f, 0f);
            stripe.rectTransform.anchorMax = new Vector2(0f, 1f);
            stripe.rectTransform.sizeDelta = new Vector2(4f, -12f);
            stripe.rectTransform.anchoredPosition = new Vector2(4f, 0f);
            titleText = UiFactory.Text(tooltip, "Title", theme, "", 26f, theme.text, true);
            hintText = UiFactory.Text(tooltip, "Hint", theme, "", 20f, theme.textDim);

            toast = UiFactory.Image(transform, "Toast", theme.panel, theme.rounded);
            // Above the tool hotbar and its controls line.
            toast.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 205f), new Vector2(560f, 52f));
            toastText = UiFactory.Text(toast.transform, "Text", theme, "", 24f, theme.text, true, TextAlignmentOptions.Center);
            toastText.rectTransform.Fill(16f, 0f, 16f, 0f);
            toast.gameObject.SetActive(false);

            HideTooltip();
        }

        void Update()
        {
            if (toast.gameObject.activeSelf && Time.unscaledTime > toastUntil)
                toast.gameObject.SetActive(false);
        }

        public void SetReticle(Sprite sprite, bool large, Color tint)
        {
            reticle.sprite = sprite != null ? sprite : theme.reticleDot;
            reticle.color = tint;
            reticle.rectTransform.sizeDelta = large ? new Vector2(44f, 44f) : new Vector2(24f, 24f);
        }

        public void SetReticleVisible(bool visible) => reticle.enabled = visible;

        public void ShowTooltip(string title, string hint, bool available)
        {
            if (string.IsNullOrEmpty(title))
            {
                HideTooltip();
                return;
            }
            titleText.text = title;
            hintText.text = hint;
            hintText.gameObject.SetActive(!string.IsNullOrEmpty(hint));
            hintText.color = available ? theme.textDim : new Color(0.95f, 0.65f, 0.4f);
            tooltipGroup.alpha = 1f;
        }

        public void HideTooltip() => tooltipGroup.alpha = 0f;

        /// <summary>Short message at the bottom of the screen, e.g. "Placed C-Channel on the bench".</summary>
        public void ShowToast(string message, float seconds = 2.5f)
        {
            toastText.text = message;
            toast.gameObject.SetActive(true);
            toastText.ForceMeshUpdate();
            toast.rectTransform.sizeDelta = new Vector2(Mathf.Min(toastText.preferredWidth + 48f, 1100f), 52f);
            toastUntil = Time.unscaledTime + seconds;
        }
    }
}
