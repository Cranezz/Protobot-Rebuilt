using System;
using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Interface {
    // V2: the intro when Protobot starts (about 4 s; any key or click skips it). The field photo fades in sharp and
    // slowly pulls back, the gear spins in to the motor's whine, slides aside as "Protobot" appears with the version
    // under it, then the photo blurs into the home menu's own blurred background and the menu fades up in front.
    // Not shown when the app was started to do something (open a build, join or host a match, a benchmark).
    public class Intro : MonoBehaviour {
        private static bool shown;
        private const float Total = 4.1f;

        private CanvasGroup all, logo, menu;
        private RawImage photo, blur, gear, word;
        private RectTransform photoRect, gearRect, wordRect, logoRect;
        private TextMeshProUGUI subtitle;
        private Image tint, line;
        private AudioSource whine;
        private float time;
        private bool skipping;

        // (Play mode in the editor keeps statics between runs: each run starts fresh.)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => shown = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Make() {
            if (shown || !Application.isPlaying) return;
            shown = true;
            var args = Environment.GetCommandLineArgs();
            if (args.Any(a => a == "-open" || a == "-join" || a == "-host" || a == "-benchmark" || a == "-autostart" || a.StartsWith("--veloapp-"))) return;
            new GameObject("Intro").AddComponent<Intro>();
        }

        private void Start() {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
            all = gameObject.AddComponent<CanvasGroup>();
            var root = (RectTransform)transform;

            var black = Ui.Image(root, "Black", Color.black, 0f, true);   // also takes the clicks while it plays
            black.rectTransform.Fill();
            photo = Picture(root, "Photo", Online.Backgrounds.Sharp());
            photoRect = photo != null ? photo.rectTransform : null;
            blur = Picture(root, "Blurred", Online.Backgrounds.Blurred());
            if (blur != null) blur.color = new Color(1f, 1f, 1f, 0f);
            tint = Ui.Image(root, "Tint", new Color(0.03f, 0.04f, 0.06f, 0.15f));
            tint.rectTransform.Fill();
            var shade = Ui.Rect("Shade", root).Fill().gameObject.AddComponent<RawImage>();
            shade.texture = Online.Backgrounds.Shade();
            shade.raycastTarget = false;
            shade.color = new Color(1f, 1f, 1f, 0.6f);

            logoRect = Ui.Rect("Logo", root);
            logoRect.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(900f, 300f));
            logo = logoRect.gameObject.AddComponent<CanvasGroup>();
            var gearTexture = Resources.Load<Texture2D>("Intro/Gear");
            var wordTexture = Resources.Load<Texture2D>("Intro/Wordmark");
            gear = Ui.Rect("Gear", logoRect).gameObject.AddComponent<RawImage>();
            gear.texture = gearTexture;
            gear.raycastTarget = false;
            gearRect = gear.rectTransform;
            var gearAspect = gearTexture != null ? (float)gearTexture.width / gearTexture.height : 0.9f;
            gearRect.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220f * gearAspect, 220f));
            word = Ui.Rect("Word", logoRect).gameObject.AddComponent<RawImage>();
            word.texture = wordTexture;
            word.raycastTarget = false;
            wordRect = word.rectTransform;
            var wordAspect = wordTexture != null ? (float)wordTexture.width / wordTexture.height : 5f;
            wordRect.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(95f, 0f), new Vector2(118f * wordAspect, 118f));
            line = Ui.Image(logoRect, "Line", Theme.Accent, 2f);
            line.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -100f), new Vector2(0f, 3f));
            subtitle = Ui.Text(logoRect, "Subtitle", "Rebuilt  ·  " + Builds.BuildInfo.Display, 26f, new Color(1f, 1f, 1f, 0f), Theme.Medium, TextAlignmentOptions.Center);
            subtitle.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -135f), new Vector2(900f, 40f));

            // The menu waits behind, faded out, and comes up at the end.
            var launch = GameObject.Find("Launch Canvas");
            var splash = launch != null ? launch.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Splash Screen") : null;
            if (splash != null) {
                menu = splash.gameObject.GetOrAdd<CanvasGroup>();
                menu.alpha = 0f;
            }

            whine = gameObject.AddComponent<AudioSource>();
            whine.clip = Audio.Sounds.MotorLoop;
            whine.loop = true;
            whine.playOnAwake = false;
            whine.volume = 0f;
            whine.spatialBlend = 0f;
            whine.Play();
            Apply(0f);
        }

        private static RawImage Picture(RectTransform root, string name, Texture2D texture) {
            if (texture == null) return null;
            var holder = Ui.Rect(name + " Holder", root).Fill();
            var image = Ui.Rect(name, holder).gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            var fit = image.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = (float)texture.width / texture.height;
            return image;
        }

        private static float Ease(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
        private static float Smooth(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
        private static float Span(float t, float from, float to) => Mathf.Clamp01((t - from) / (to - from));

        // A little overshoot (the gear settling in).
        private static float Back(float t) {
            t = Mathf.Clamp01(t);
            const float s = 1.6f;
            t -= 1f;
            return t * t * ((s + 1f) * t + s) + 1f;
        }

        private void Apply(float t) {
            // The photo: fades in, pulls back slowly the whole time, then blurs into the menu's background.
            if (photo != null) {
                photo.color = new Color(1f, 1f, 1f, Smooth(Span(t, 0f, 0.8f)));
                var pull = Ease(t / Total);
                photoRect.localScale = Vector3.one * Mathf.Lerp(1.32f, 1.06f, pull);
                photoRect.anchoredPosition = new Vector2(Mathf.Lerp(40f, 0f, pull), Mathf.Lerp(-20f, 0f, pull));
            }
            var toMenu = Smooth(Span(t, 2.7f, 3.5f));
            if (blur != null) {
                blur.color = new Color(1f, 1f, 1f, toMenu);
                blur.rectTransform.localScale = photoRect != null ? photoRect.localScale : Vector3.one;
            }
            // Darker behind the logo (it reads over the busy field), then the menu's own tint.
            var behind = Smooth(Span(t, 0.3f, 1.2f)) * (1f - toMenu);
            tint.color = new Color(0.03f, 0.04f, 0.06f, Mathf.Lerp(0.15f, 0.38f, toMenu) + 0.22f * behind);

            // The gear spins in (with the motor's whine), then slides left for the name.
            var spin = Span(t, 0.35f, 1.35f);
            var aside = Smooth(Span(t, 1.25f, 1.9f));
            gear.color = new Color(1f, 1f, 1f, Smooth(Span(t, 0.35f, 0.75f)));
            gearRect.localScale = Vector3.one * Mathf.Lerp(Mathf.LerpUnclamped(0.35f, 1f, Back(spin)), 0.62f, aside);
            gearRect.localEulerAngles = new Vector3(0f, 0f, Mathf.Lerp(-200f, 0f, Ease(spin)));
            gearRect.anchoredPosition = new Vector2(Mathf.Lerp(0f, -265f, aside), 0f);
            word.color = new Color(1f, 1f, 1f, aside);
            wordRect.anchoredPosition = new Vector2(Mathf.Lerp(150f, 70f, aside), 0f);
            var under = Smooth(Span(t, 1.8f, 2.4f));
            line.rectTransform.sizeDelta = new Vector2(Mathf.Lerp(0f, 520f, under), 3f);
            subtitle.color = new Color(1f, 1f, 1f, 0.85f * under);

            // Out: the logo rises and fades as the photo blurs; then everything fades and the menu comes up.
            var leave = Smooth(Span(t, 2.6f, 3.3f));
            logo.alpha = 1f - leave;
            logoRect.anchoredPosition = new Vector2(0f, 30f + 60f * leave);
            logoRect.localScale = Vector3.one * Mathf.Lerp(1f, 0.92f, leave);
            var fade = Smooth(Span(t, 3.4f, Total));
            all.alpha = 1f - fade;
            if (menu != null) {
                menu.alpha = fade;
                menu.transform.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, Ease(Span(t, 3.4f, Total)));
            }

            // The whine: up while the gear spins, gone as it settles.
            if (whine != null) {
                var level = Mathf.Sin(Mathf.PI * Span(t, 0.3f, 1.6f));
                whine.volume = 0.22f * level;
                whine.pitch = Mathf.Lerp(0.5f, 1.35f, Ease(Span(t, 0.3f, 1.4f)));
            }
        }

        private void Update() {
            time += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);   // a loading hitch doesn't skip ahead
            // Skipping jumps to the end of the logo, into the blur and the menu.
            var pressed = (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                          || (Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame));
            if (pressed && !skipping && time < 2.7f) {
                skipping = true;
                time = 2.7f;
            }
            Apply(time);
            if (time >= Total) {
                if (menu != null) {
                    menu.alpha = 1f;
                    menu.transform.localScale = Vector3.one;
                }
                Destroy(gameObject);
            }
        }
    }
}
