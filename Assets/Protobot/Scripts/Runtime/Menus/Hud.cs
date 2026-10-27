using Protobot.Audio;
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
        Image toastStripe;
        CanvasGroup toastGroup;
        TextMeshProUGUI toastText;
        float toastShown;
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
            // Above the tool hotbar and its controls line, and drawn over menus too (e.g. the part picker).
            toast.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 205f), new Vector2(560f, 52f));
            var toastCanvas = toast.gameObject.AddComponent<Canvas>();
            toastCanvas.overrideSorting = true;
            toastCanvas.sortingOrder = 60;
            toastGroup = toast.gameObject.AddComponent<CanvasGroup>();
            toastGroup.blocksRaycasts = false;
            toastStripe = UiFactory.Image(toast.transform, "Kind", theme.accent, theme.rounded);
            toastStripe.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(6f, 32f));
            toastText = UiFactory.Text(toast.transform, "Text", theme, "", 24f, theme.text, true, TextAlignmentOptions.Center);
            toastText.rectTransform.Fill(22f, 0f, 16f, 0f);
            toast.gameObject.SetActive(false);

            HideTooltip();
        }

        void Update()
        {
            if (!toast.gameObject.activeSelf)
                return;
            var now = Time.unscaledTime;
            if (now > toastUntil)
            {
                toast.gameObject.SetActive(false);
                return;
            }
            // Pops in, fades out.
            var age = now - toastShown;
            toast.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.88f, 1f, Mathf.SmoothStep(0f, 1f, age / 0.12f));
            toastGroup.alpha = Mathf.Clamp01((toastUntil - now) / 0.3f);
        }

        public void SetReticle(Sprite sprite, bool large, Color tint)
        {
            reticle.sprite = sprite != null ? sprite : theme.reticleDot;
            reticle.color = tint;
            // Tool crosshairs are big; the plain dot only grows a little over something usable.
            var size = sprite != null ? (large ? 44f : 24f) : (large ? 30f : 22f);
            reticle.rectTransform.sizeDelta = new Vector2(size, size);
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

        RectTransform banner;
        TextMeshProUGUI bannerTitle;
        TextMeshProUGUI bannerHelp;

        /// <summary>A panel at the top of the screen for a mode the player is in (e.g. rotating a part) and its keys.</summary>
        public void ShowBanner(string title, string help)
        {
            if (banner == null)
            {
                var back = UiFactory.Image(transform, "Banner", new Color(theme.panel.r, theme.panel.g, theme.panel.b, 0.92f), theme.rounded);
                banner = back.rectTransform;
                banner.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(900f, 88f));
                var stripes = UiFactory.Hazard(banner, "Stripes", theme);
                stripes.rectTransform.anchorMin = new Vector2(0f, 1f);
                stripes.rectTransform.anchorMax = new Vector2(1f, 1f);
                stripes.rectTransform.offsetMin = new Vector2(12f, -16f);
                stripes.rectTransform.offsetMax = new Vector2(-12f, -8f);
                bannerTitle = UiFactory.Text(banner, "Title", theme, "", 30f, theme.text, true, TextAlignmentOptions.Center);
                bannerTitle.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(860f, 36f));
                bannerHelp = UiFactory.Text(banner, "Help", theme, "", 20f, theme.textDim, false, TextAlignmentOptions.Center);
                bannerHelp.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -54f), new Vector2(860f, 26f));
            }
            bannerTitle.text = title;
            bannerHelp.text = help;
            banner.gameObject.SetActive(true);
        }

        public void HideBanner()
        {
            if (banner != null)
                banner.gameObject.SetActive(false);
        }

        public enum ToastKind { Info, Success, Error }

        /// <summary>Short message at the bottom of the screen, e.g. "Placed C-Channel on the bench".</summary>
        public void ShowToast(string message, float seconds = 2.5f, ToastKind kind = ToastKind.Info)
        {
            if (string.IsNullOrEmpty(message))
                return;
            toastText.text = message;
            toast.gameObject.SetActive(true);
            toastText.ForceMeshUpdate();
            toast.rectTransform.sizeDelta = new Vector2(Mathf.Min(toastText.preferredWidth + 60f, 1100f), 52f);
            toastStripe.color = kind switch
            {
                ToastKind.Success => new Color(0.35f, 0.85f, 0.45f),
                ToastKind.Error => new Color(0.95f, 0.35f, 0.3f),
                _ => theme.accent,
            };
            toastShown = Time.unscaledTime;
            toastUntil = toastShown + seconds;
        }

        /// <summary>Something the player tried can't be done: say why, with the error sound.</summary>
        public void ShowError(string message)
        {
            ShowToast(message, 3f, ToastKind.Error);
            Sounds.Play(Sounds.Error, 0.5f);
        }

        /// <summary>Something worked (a part was placed, thrown away...): say so, with a sound.</summary>
        public void ShowSuccess(string message, AudioClip sound = null)
        {
            ShowToast(message, 2.5f, ToastKind.Success);
            Sounds.Play(sound != null ? sound : Sounds.Pop, 0.45f);
        }
    }
}
