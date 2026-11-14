using System.Collections.Generic;
using System.Linq;
using Protobot.Interface;
using Protobot.Rendering;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Game {
    /// <summary>One thing that scored, where it is on the field (scoring lists them for the reveal).</summary>
    public class ScoredItem {
        public Vector3 point;       // world
        public int points;
        public string kind;         // "Red/blue pin halves", "Yellow pin halves", "Robot in the Midfield"
        public string where;        // "Left Quadrant goal", "Midfield Goal", "Midfield"
        public Pin pin;
    }

    /// <summary>V2: the end of a run, like a game's score screen: the camera circles the field, each thing that scored
    /// lights up blue in turn with its points over it, and the points lift off and fly into the total in the middle of
    /// the screen, which counts up as they land; then the breakdown (what scored and where) and the buttons appear.
    /// Any key or click skips to the end.</summary>
    public class ScoreReveal : MonoBehaviour {
        private static readonly Color Glow = new(0.3f, 0.65f, 1f, 1f);

        private SkillsGame game;
        private bool newBest;
        private RectTransform root, panel, totalRect, breakdown, buttons;
        private TextMeshProUGUI totalText;
        private readonly List<Flyer> flyers = new();
        private readonly List<Renderer> outlined = new();
        private float start, shown, target, pulse, tickTimer;
        private AudioSource ticker;
        private int landed;
        private bool done;
        private Vector3 fromFocus, fromAngles;
        private float fromDistance;

        private class Flyer {
            public ScoredItem item;
            public float at;                 // when it lights up (seconds after start)
            public RectTransform halo, label;
            public Vector2 launch;           // where the label was when it took off
            public bool lit, flying, arrived;
        }

        private const float LightFor = 0.35f, HoverFor = 0.45f, FlyFor = 0.65f, FirstAt = 1.3f;

        public static void Play(SkillsGame game, bool newBest) {
            var rect = Ui.Rect("Score Reveal", Interface.Interface.Root).Fill();
            rect.pivot = new Vector2(0.5f, 0.5f);
            var reveal = rect.gameObject.AddComponent<ScoreReveal>();
            reveal.game = game;
            reveal.newBest = newBest;
            reveal.root = rect;
            reveal.Build();
        }

        private void Build() {
            start = Time.unscaledTime;
            GameUi.HideHud();
            if (Motion.Sandbox.Instance != null) Motion.Sandbox.Instance.Follow = false;
            var camera = PivotCamera.Main;
            if (camera != null) {
                fromFocus = camera.transform.position;
                fromAngles = camera.transform.eulerAngles;
                fromDistance = camera.camera.transform.localPosition.z;
            }

            // The total, in a glass card at the top middle.
            panel = Online.OnlineWindow.GlassPanel(root, "Score", new Color(0.07f, 0.08f, 0.1f, 0.72f), 16f).rectTransform;
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1f);
            panel.pivot = new Vector2(0.5f, 1f);
            panel.anchoredPosition = new Vector2(0f, -(Layout.TopBarBottom + 24f));
            panel.sizeDelta = new Vector2(560f, 0f);
            Ui.Column(panel, 4f, new RectOffset(28, 28, 18, 20)).childAlignment = TextAnchor.UpperCenter;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var title = Ui.Text(panel, "Title", game.ModeName.ToUpperInvariant() + "  ·  SCORE", 13f, new Color(1f, 1f, 1f, 0.6f), Theme.Medium, TextAlignmentOptions.Center);
            title.characterSpacing = 6f;
            title.Size(-1, 22f);
            totalText = Ui.Text(panel, "Total", "0", 96f, Color.white, Theme.Bold, TextAlignmentOptions.Center);
            totalText.overflowMode = TextOverflowModes.Overflow;
            totalText.Size(-1, 110f);
            totalRect = totalText.rectTransform;
            breakdown = Ui.Rect("Breakdown", panel);
            Ui.Column(breakdown, 3f);
            breakdown.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            buttons = Ui.Rect("Buttons", panel);
            Ui.Row(buttons, 10f, new RectOffset(0, 0, 10, 0), TextAnchor.MiddleCenter);
            buttons.Size(-1, 50f);
            buttons.gameObject.SetActive(false);
            var group = panel.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            // What scored, one after another: slow at first, then faster and faster.
            var items = game.score.items;
            var at = FirstAt;
            for (var i = 0; i < items.Count; i++) {
                flyers.Add(new Flyer { item = items[i], at = at });
                at += Mathf.Max(0.045f, 0.55f * Mathf.Pow(0.8f, i));
            }
            // The counting tick: its own 2D source so its pitch can climb.
            ticker = gameObject.AddComponent<AudioSource>();
            ticker.playOnAwake = false;
            ticker.spatialBlend = 0f;
            target = 0f;
        }

        private void Update() {
            if (game == null || SkillsGame.Current != game || game.state != SkillsGame.State.Results) {
                Destroy(gameObject);
                return;
            }
            var t = Time.unscaledTime - start;
            Orbit(t);
            var group = panel.GetComponent<CanvasGroup>();
            group.alpha = Mathf.Clamp01((t - 0.3f) / 0.4f);

            if (!done && Skipped()) SkipToEnd();

            var targetPoint = Local(RectTransformUtility.WorldToScreenPoint(null, totalRect.position));
            foreach (var f in flyers) {
                if (f.arrived || t < f.at) continue;
                var since = t - f.at;
                if (!f.lit) Light(f);
                var screen = Camera.main != null ? Camera.main.WorldToScreenPoint(f.item.point) : Vector3.zero;
                var behind = screen.z < 0f;
                var at = Local(screen);
                // The halo: grows in, then breathes while the points hover, then fades as they leave.
                var grow = Mathf.Clamp01(since / LightFor);
                var fade = 1f - Mathf.Clamp01((since - LightFor - HoverFor) / FlyFor);
                f.halo.gameObject.SetActive(!behind);
                f.halo.anchoredPosition = at;
                f.halo.localScale = Vector3.one * (0.4f + 0.6f * EaseOut(grow)) * (1f + 0.08f * Mathf.Sin(since * 9f));
                f.halo.GetComponent<Image>().color = new Color(Glow.r, Glow.g, Glow.b, 0.85f * fade);
                // The points: rise over it, then fly into the total.
                if (since < LightFor + HoverFor) {
                    var rise = EaseOut(Mathf.Clamp01(since / (LightFor + HoverFor)));
                    f.label.anchoredPosition = at + new Vector2(0f, 26f + 34f * rise);
                    f.label.localScale = Vector3.one * Mathf.Lerp(0.5f, 1f, EaseOut(Mathf.Clamp01(since / LightFor)));
                    f.label.gameObject.SetActive(!behind);
                    f.launch = f.label.anchoredPosition;
                }
                else {
                    var k = Mathf.Clamp01((since - LightFor - HoverFor) / FlyFor);
                    var e = k * k * (3f - 2f * k);
                    // A curve: up and over, into the number.
                    var control = Vector2.Lerp(f.launch, targetPoint, 0.35f) + new Vector2(0f, 160f);
                    var p = Vector2.Lerp(Vector2.Lerp(f.launch, control, e), Vector2.Lerp(control, targetPoint, e), e);
                    f.label.anchoredPosition = p;
                    f.label.localScale = Vector3.one * Mathf.Lerp(1f, 0.55f, e);
                    f.label.gameObject.SetActive(true);
                    if (k >= 1f) Land(f);
                }
            }

            // The total counts up a point at a time, a tick each, slowly at first and faster and faster as it climbs.
            if (shown < target) {
                tickTimer -= Time.unscaledDeltaTime;
                while (tickTimer <= 0f && shown < target) {
                    var final = Mathf.Max(1f, game.score.items.Sum(i => i.points));
                    var progress = Mathf.Clamp01(shown / final);
                    var step = Mathf.Max(1f, Mathf.Ceil((target - shown) / 40f));   // never far behind the flying points
                    shown = Mathf.Min(target, shown + step);
                    tickTimer += Mathf.Lerp(0.16f, 0.012f, Mathf.Pow(progress, 0.6f));
                    pulse = Mathf.Max(pulse, 0.3f);
                    Tick(progress);
                }
            }
            totalText.text = Mathf.RoundToInt(shown).ToString();
            pulse = Mathf.MoveTowards(pulse, 0f, Time.unscaledDeltaTime * 3f);
            totalRect.localScale = Vector3.one * (1f + 0.12f * pulse);
            totalText.color = Color.Lerp(Color.white, Glow, pulse);

            if (!done && flyers.All(f => f.arrived) && shown >= target && t > (flyers.Count > 0 ? flyers[^1].at + LightFor + HoverFor + FlyFor + 0.25f : 1.2f)) ShowBreakdown();
        }

        private void Light(Flyer f) {
            f.lit = true;
            var halo = Ui.Image(root, "Glow", Glow);
            halo.sprite = HaloSprite();
            halo.raycastTarget = false;
            f.halo = halo.rectTransform;
            f.halo.anchorMin = f.halo.anchorMax = new Vector2(0.5f, 0.5f);
            f.halo.sizeDelta = new Vector2(90f, 90f);
            f.halo.SetAsFirstSibling();
            var label = Ui.Text(root, "Points", "+" + f.item.points, 30f, Color.white, Theme.Bold, TextAlignmentOptions.Center);
            label.overflowMode = TextOverflowModes.Overflow;
            label.outlineWidth = 0.18f;
            label.outlineColor = new Color32(20, 70, 160, 255);
            f.label = label.rectTransform;
            f.label.anchorMin = f.label.anchorMax = new Vector2(0.5f, 0.5f);
            f.label.sizeDelta = new Vector2(120f, 40f);
            if (f.item.pin != null)
                foreach (var r in f.item.pin.GetComponentsInChildren<Renderer>()) {
                    OutlineFeature.SetOutline(r, true, Glow);
                    outlined.Add(r);
                }
            Audio.Sounds.Play(Audio.Sounds.Pop, 0.35f);
        }

        private void Land(Flyer f) {
            f.arrived = true;
            if (f.label != null) Destroy(f.label.gameObject);
            if (f.halo != null) Destroy(f.halo.gameObject);
            target += f.item.points;
            landed++;
            pulse = 1f;
        }

        private float lastTick;

        // A tick per point added, climbing in pitch as the total climbs (at most one every 15 ms, or it's a buzz).
        private void Tick(float progress) {
            if (ticker == null || Time.unscaledTime - lastTick < 0.015f) return;
            lastTick = Time.unscaledTime;
            ticker.pitch = Mathf.Lerp(0.9f, 1.7f, progress);
            ticker.PlayOneShot(Audio.Sounds.Tick, 0.75f);
        }

        private bool Skipped() {
            if (Time.unscaledTime - start < 0.4f) return false;
            var k = Keyboard.current;
            var m = Mouse.current;
            return (k != null && (k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame || k.escapeKey.wasPressedThisFrame))
                   || (m != null && m.leftButton.wasPressedThisFrame && !buttons.gameObject.activeSelf);
        }

        private void SkipToEnd() {
            foreach (var f in flyers) {
                if (f.arrived) continue;
                if (!f.lit) Light(f);
                Land(f);
            }
            shown = target;
            ShowBreakdown();
        }

        // The camera: from where it was, round the field slowly.
        private void Orbit(float t) {
            var camera = PivotCamera.Main;
            if (camera == null || game.field == null) return;
            // Close, as if sitting just above the field walls looking down: about 61" out from the middle and 55" up
            // (the walls are ~70" out), round the middle.
            var focus = game.field.ToWorld(Vector2.zero, 4f);
            var blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 1.6f));
            var yaw = fromAngles.y + t * 8f;
            var angles = new Vector3(Mathf.LerpAngle(fromAngles.x, 40f, blend), yaw, 0f);
            camera.SetTransform(Vector3.Lerp(fromFocus, focus, blend), angles, Mathf.Lerp(fromDistance, -80f, blend));
        }

        // ---- the breakdown and the buttons

        private void ShowBreakdown() {
            if (done) return;
            done = true;
            var s = game.score;
            var rows = new List<(string label, string count, int points, bool heading)>();
            if (s.match) {   // V2: an online match
                rows.Add((s.red > s.blue ? "Red wins" : s.blue > s.red ? "Blue wins" : "A tie", "", -1, true));
                rows.Add(("Red Alliance", "", s.red, false));
                rows.Add(("Blue Alliance", "", s.blue, false));
                rows.Add(("Your Alliance's points", "", -1, true));
            }
            if (s.items.Count == 0) rows.Add(("No points this time", "", -1, false));
            foreach (var group in s.items.GroupBy(i => i.kind)) {
                var each = group.First().points;
                var count = group.Count();
                rows.Add((group.Key, count > 1 || each != group.Sum(g => g.points) ? count + " × " + each : "", group.Sum(g => g.points), false));
            }
            var places = s.items.Where(i => !string.IsNullOrEmpty(i.where)).GroupBy(i => i.where).ToList();
            if (places.Count > 0) {
                rows.Add(("Where", "", -1, true));
                foreach (var place in places.OrderByDescending(p => p.Sum(i => i.points)))
                    rows.Add((place.Key, place.Count() + (place.Count() == 1 ? " scored" : " scored"), place.Sum(i => i.points), false));
            }
            var delay = 0f;
            foreach (var (label, count, points, heading) in rows) {
                var row = Ui.Rect("Row", breakdown);
                Ui.Row(row, 8f);
                row.Size(-1, heading ? 28f : 26f);
                if (heading) {
                    var h = Ui.Text(row, "Heading", label.ToUpperInvariant(), 12f, new Color(1f, 1f, 1f, 0.5f), Theme.Medium);
                    h.characterSpacing = 5f;
                    h.Size(-1, 28f, 1f);
                }
                else {
                    Ui.Text(row, "Label", label, Theme.FontBody, Color.white).Size(-1, 26f, 1f);
                    Ui.Text(row, "Count", count, Theme.FontSmall, new Color(1f, 1f, 1f, 0.6f), null, TextAlignmentOptions.MidlineRight).Size(90f, 26f);
                    Ui.Text(row, "Points", points >= 0 ? points.ToString() : "", Theme.FontBody, Glow, Theme.Bold, TextAlignmentOptions.MidlineRight).Size(60f, 26f);
                }
                var fade = row.gameObject.AddComponent<FadeIn>();
                fade.delay = delay;
                delay += 0.12f;
            }
            var placed = Ui.Text(breakdown, "Placed", $"{s.placedPins} pins and {s.placedCups} cups placed", Theme.FontSmall, new Color(1f, 1f, 1f, 0.45f), null, TextAlignmentOptions.Center);
            placed.Size(-1, 24f);
            placed.gameObject.AddComponent<FadeIn>().delay = delay;
            var best = Ui.Text(breakdown, "Best", newBest ? "New best for this robot!" : $"Best for this robot: {game.best}", Theme.FontBody,
                newBest ? Theme.Success : new Color(1f, 1f, 1f, 0.6f), Theme.Medium, TextAlignmentOptions.Center);
            best.Size(-1, 28f);
            best.gameObject.AddComponent<FadeIn>().delay = delay + 0.1f;

            buttons.gameObject.SetActive(true);
            buttons.gameObject.AddComponent<FadeIn>().delay = delay + 0.2f;
            if (game.online) {   // V2: an online match goes back to its lobby (the host starts the next one)
                var lobby = Ui.Button(buttons, "Lobby", "Back to the lobby", game.Exit, true, 8f);
                lobby.Size(200f, 40f);
                Audio.Sounds.Play(Audio.Sounds.Pop, 0.5f);
                return;
            }
            var exit = Ui.Button(buttons, "Exit", "Exit game", game.Exit, false, 8f);
            exit.targetGraphic.color = new Color(1f, 1f, 1f, 0.1f);
            exit.Size(140f, 40f);
            var change = Ui.Button(buttons, "Setup", "Change start", game.BackToSetup, false, 8f);
            change.targetGraphic.color = new Color(1f, 1f, 1f, 0.1f);
            change.Size(140f, 40f);
            var again = Ui.Button(buttons, "Again", "Run again", game.StartRunNextFrame, true, 8f);
            again.Size(140f, 40f);
            Audio.Sounds.Play(Audio.Sounds.Pop, 0.5f);
        }

        private void OnDestroy() {
            foreach (var r in outlined)
                if (r != null) OutlineFeature.SetOutline(r, false);
        }

        // ---- helpers

        private Vector2 Local(Vector2 screen) {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out var local);
            return local;
        }

        private static float EaseOut(float x) => 1f - (1f - x) * (1f - x);

        private static Sprite halo;

        // A soft round glow (bright middle, fading out).
        private static Sprite HaloSprite() {
            if (halo != null) return halo;
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Glow" };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++) {
                    var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                    var a = Mathf.Clamp01(1f - d);
                    a = a * a * (0.6f + 0.4f * a);
                    var ring = Mathf.Exp(-Mathf.Pow((d - 0.55f) / 0.08f, 2f)) * 0.5f;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a + ring) * 255));
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            return halo = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Fades a row in after a delay.</summary>
        private class FadeIn : MonoBehaviour {
            public float delay;
            private float born;
            private CanvasGroup group;

            private void Start() {
                born = Time.unscaledTime;
                group = gameObject.GetOrAdd<CanvasGroup>();
                group.alpha = 0f;
            }

            private void Update() {
                var k = Mathf.Clamp01((Time.unscaledTime - born - delay) / 0.25f);
                group.alpha = k;
                if (k >= 1f) Destroy(this);
            }
        }
    }
}
