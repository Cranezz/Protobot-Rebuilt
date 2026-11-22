using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Interface {
    // V2: the launch window (the original's home menu) in the new interface's colours, laid out as it was: the panel
    // and text in Theme's graphite, rows that light up under the mouse, the version and build number (BuildInfo)
    // instead of the original's "Beta 1.3.7", a play icon for Play game, recent builds by name (the path in a tip), and
    // "Report an Issue" opening the bug report (with the session's log) instead of the original project's GitHub.
    public class LaunchRestyle : MonoBehaviour {
        private Transform launch, splashPanel;
        private GameObject creditsPanel, recentBuilds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Make() {
            if (!Application.isPlaying) return;
            new GameObject("Launch Restyle").AddComponent<LaunchRestyle>();
        }

        private IEnumerator Start() {
            yield return null;   // after GameUi adds Play game
            var canvas = GameObject.Find("Launch Canvas");
            if (canvas == null) {
                Destroy(gameObject);
                yield break;
            }
            launch = canvas.transform;
            Apply();
        }

        private void Apply() {
            var splash = launch.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Splash Screen");
            splashPanel = splash;
            // V2: the Multiplayer screen's look: a blurred, slowly drifting picture of the field behind, and the window
            // itself see-through (smoky glass with a light edge); its layout, picture and logo stay as they were.
            var tint = launch.Find("Background Tint");
            if (tint != null && tint.TryGetComponent(out Image tintImage)) {
                var holder = Ui.Rect("Blurred Background", launch).Fill();
                holder.SetSiblingIndex(tint.GetSiblingIndex());
                holder.gameObject.AddComponent<RectMask2D>();
                var picture = Online.Backgrounds.Blurred();
                if (picture != null) {
                    var raw = Ui.Rect("Picture", holder).gameObject.AddComponent<RawImage>();
                    raw.texture = picture;
                    raw.raycastTarget = false;
                    var fit = raw.gameObject.AddComponent<AspectRatioFitter>();
                    fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                    fit.aspectRatio = (float)picture.width / picture.height;
                    raw.gameObject.AddComponent<Online.BackgroundDrift>();
                    tintImage.color = new Color(0.03f, 0.04f, 0.06f, 0.38f);
                }
            }
            if (splash != null && splash.TryGetComponent(out Image panel)) {
                panel.color = new Color(0.07f, 0.08f, 0.1f, 0.55f);
                var edge = panel.gameObject.GetOrAdd<Outline>();
                edge.effectColor = new Color(1f, 1f, 1f, 0.12f);
                edge.effectDistance = new Vector2(1f, -1f);
            }
            foreach (var text in launch.GetComponentsInChildren<TMP_Text>(true)) {
                if (text.name == "Version") {
                    text.text = Builds.BuildInfo.Display;
                    text.fontSize = Mathf.Min(text.fontSize, 15f);
                    text.textWrappingMode = TextWrappingModes.NoWrap;
                    text.overflowMode = TextOverflowModes.Overflow;
                }
                else if (text.name == "Title") text.color = Theme.TextDim;
                else if (text.name == "Legal Text") text.color = Theme.TextFaint;
                else if (text.color.r > 0.9f && text.color.g > 0.9f) text.color = Theme.Text;
            }
            foreach (var button in launch.GetComponentsInChildren<Button>(true)) Restyle(button);
            var buttons = launch.GetComponentsInChildren<Button>(true);
            var play = buttons.FirstOrDefault(b => b.name == "Play Game Button");
            if (play != null && play.transform.Find("Icon") is Transform playIcon && playIcon.TryGetComponent(out Image image)) {
                image.sprite = PlaySprite();
                image.color = Color.white;
            }
            var multiplayer = buttons.FirstOrDefault(b => b.name == "Multiplayer Button");
            if (multiplayer != null && multiplayer.transform.Find("Icon") is Transform peopleIcon && peopleIcon.TryGetComponent(out Image people)) {
                people.sprite = PeopleSprite();
                people.color = Color.white;
            }
            var report = buttons.FirstOrDefault(b => b.name == "Report Bugs Button");
            if (report != null) {
                var bug = launch.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Report Bug Button");
                if (bug != null && bug.TryGetComponent(out Image bugImage) && report.transform.Find("Icon") is Transform icon && icon.TryGetComponent(out Image reportImage))
                    reportImage.sprite = bugImage.sprite;
                var label = report.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = "Report a bug";
                report.onClick = new Button.ButtonClickedEvent();
                report.onClick.AddListener(() => Builds.CrashGuard.ReportDialog(false));
            }
            // V2: Patch Notes shows this version's notes (the original's button opened its own project's page).
            var notes = buttons.FirstOrDefault(b => b.name == "Patch Notes Button");
            if (notes != null) {
                notes.onClick = new Button.ButtonClickedEvent();
                notes.onClick.AddListener(PatchNotes.Open);
            }
            // V2: the credits name this version's work and what it's made from, under the original's.
            var credits = launch.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "Credits List");
            if (credits != null) {
                creditsPanel = credits.transform.parent.gameObject;
                recentBuilds = launch.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Builds Menu")?.gameObject;
            }
            if (credits != null && !credits.text.Contains("Rebuilt V2"))
                credits.text += "\n<line-height=125%>\n<line-height=100%><size=100%>Protobot Rebuilt V2:<line-height=100%>\n<size=65%>Crane | 11777C\n"
                                + "Parts: VEX-CAD library · Field: VEX Robotics\nUnity · DOTween · Velopack";
        }

        private static void Restyle(Button button) {
            if (button.name.StartsWith("Close Button")) return;
            if (button.targetGraphic is Image image) image.color = Color.white;
            var colors = button.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0f);
            colors.selectedColor = new Color(1f, 1f, 1f, 0f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.12f);   // V2: see-through like the panel
            colors.pressedColor = new Color(1f, 1f, 1f, 0.2f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.transition = Selectable.Transition.ColorTint;
        }

        // Recent builds: the list is remade when the window opens, so its names are tidied as they come.
        private float next;

        private void Update() {
            // V2: the 3D view isn't drawn while the home menu covers it.
            if (splashPanel != null) Online.ViewHider.Set(this, splashPanel.gameObject.activeInHierarchy);
            // V2: the credits are longer now: the recent builds step aside while they show.
            if (creditsPanel != null && recentBuilds != null && recentBuilds.activeSelf == creditsPanel.activeInHierarchy) recentBuilds.SetActive(!creditsPanel.activeInHierarchy);
            if (launch == null || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.3f;
            foreach (var button in launch.GetComponentsInChildren<Button>()) {
                if (!button.name.StartsWith("IconTextButtonBUILD")) continue;
                var label = button.GetComponentInChildren<TMP_Text>();
                if (label == null || button.GetComponent<TipSource>() != null) continue;
                var path = label.text;
                Restyle(button);
                if (path.Contains("/") || path.Contains("\\") || path.EndsWith(".pbb")) {
                    label.text = Path.GetFileNameWithoutExtension(path);
                    Ui.Tip(button, label.text, null, path);
                }
                else button.gameObject.AddComponent<TipSource>().title = path;
            }
        }

        private static Sprite play, peopleSprite;

        // Two people (heads and shoulders), for Multiplayer.
        private static Sprite PeopleSprite() {
            if (peopleSprite != null) return peopleSprite;
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "People icon" };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++) {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    var alpha = 0f;
                    // Back person (right, a little smaller), then the front one over it with a gap round it.
                    var back = Person(p, new Vector2(41f, 0f), 0.82f);
                    var front = Person(p, new Vector2(25f, 0f), 1f);
                    var gap = Person(p, new Vector2(25f, 0f), 1f) - 3f;
                    alpha = Mathf.Max(Mathf.Clamp01(front + 0.5f), Mathf.Clamp01(back + 0.5f) * Mathf.Clamp01(-gap + 0.5f));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            return peopleSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        // Inside distance (pixels, negative outside) of a head over shoulders centred at x = at.x.
        private static float Person(Vector2 p, Vector2 at, float scale) {
            var head = 9f * scale - Vector2.Distance(p, new Vector2(at.x, 40f * scale + 6f));
            var shoulderCentre = new Vector2(at.x, 8f);
            var body = 17f * scale - Vector2.Distance(p, shoulderCentre);
            body = Mathf.Min(body, p.y - 8f);   // the flat bottom
            body = Mathf.Min(body, 27f * scale - p.y + 1f);
            return Mathf.Max(head, body);
        }

        // A white play triangle, like the menu's other glyphs.
        private static Sprite PlaySprite() {
            if (play != null) return play;
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Play icon" };
            var pixels = new Color32[size * size];
            Vector2 a = new(18f, 10f), b = new(18f, 54f), c = new(56f, 32f);
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++) {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    // Distance inside the triangle (negative outside), anti-aliased over a pixel.
                    float Edge(Vector2 u, Vector2 v) {
                        var n = new Vector2(v.y - u.y, u.x - v.x).normalized;
                        return Vector2.Dot(p - u, n);
                    }
                    var inside = Mathf.Min(Edge(a, b), Mathf.Min(Edge(b, c), Edge(c, a)));
                    var alpha = Mathf.Clamp01(inside + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            return play = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
