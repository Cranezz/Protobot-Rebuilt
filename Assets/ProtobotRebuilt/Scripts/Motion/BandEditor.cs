using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.Interface;
using Protobot.StateSystems;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Motion {
    // V2: what putting a band on and changing one share: its bar (which band, how many side by side, wraps), its loop
    // round posts, the stretch readout and colors, the "finger" a band is stretched to while it's dragged.
    public static class BandUi {
        public static RectTransform Bar(Transform parent, ElasticKind kind, int bands, int wraps, Action<ElasticKind> onKind,
                                        Action<int> onBands, Action<int> onWraps, Action onDone, string done = "Done") {
            var bar = Ui.Panel(parent, "Band Bar", Theme.Panel, 10f).rectTransform;
            bar.anchorMin = bar.anchorMax = new Vector2(0f, 1f);
            bar.pivot = new Vector2(0f, 1f);
            bar.anchoredPosition = new Vector2(76f, -(Layout.TopBarBottom + 60f));
            bar.sizeDelta = new Vector2(640f, 0f);
            Ui.Column(bar, 8f, new RectOffset(12, 12, 10, 10));
            bar.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            bar.GetChild(0).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var row = Ui.Rect("Row", bar);
            Ui.Row(row, 10f);
            row.Size(-1, 30f);
            var same = ElasticKind.All.Where(k => k.tubing == kind.tubing).ToList();
            Popup.Choice(row, same.Select(k => k.name).ToArray(), Mathf.Max(0, same.IndexOf(kind)), i => onKind(same[i]), 230f).Size(230f, 28f);
            Stepper(row, kind.tubing ? "Loops" : "Bands", bands, 1, 6, onBands, "Side by side: more bands pull harder.");
            Stepper(row, "Wraps", wraps, 1, 4, onWraps, "Times round the posts: each wrap is shorter, so it stretches more and pulls harder.");
            var gap = Ui.Rect("Gap", row);
            gap.Size(-1, 28f, 1f);
            Ui.Button(row, "Done", done, onDone, false, 6f).Size(-1, 28f);
            var spec = Ui.Text(bar, "Spec", kind.Size + " · " + kind.material + " · " + kind.source, Theme.FontSmall, Theme.TextDim);
            spec.Size(-1, 18f);
            return bar;
        }

        /// <summary>"Bands  − 2 +".</summary>
        public static void Stepper(Transform parent, string label, int value, int min, int max, Action<int> changed, string tip) {
            var holder = Ui.Rect(label, parent);
            Ui.Row(holder, 4f);
            holder.Size(-1, 28f);
            Ui.Text(holder, "Label", label, Theme.FontSmall, Theme.TextDim).Size(46f, 28f);
            var minus = Ui.Button(holder, "Less", "−", () => changed(Mathf.Max(min, value - 1)), false, 5f);
            minus.Size(28f, 28f);
            minus.interactable = value > min;
            Ui.Text(holder, "Value", value + (label == "Wraps" ? "x" : ""), Theme.FontBody, Theme.Text, Theme.Medium, TMPro.TextAlignmentOptions.Center).Size(26f, 28f);
            var plus = Ui.Button(holder, "More", "+", () => changed(Mathf.Min(max, value + 1)), false, 5f);
            plus.Size(28f, 28f);
            plus.interactable = value < max;
            Ui.Tip(holder, label, null, tip);
        }

        public static BandShape Shape(ElasticKind kind, IList<BandPost> posts) {
            if (posts.Count < 2) return null;
            var camera = Overlay.ViewCamera;
            var list = posts.Select(p => (p.Center, p.Axis, p.radius, p.inside, p.hole)).ToList();
            return BandShape.Of(list, camera != null ? camera.transform.forward : Vector3.up, kind.DrawThick / 2f);
        }

        /// <summary>Its natural loop: a band's by its size; tubing tied a little short of the loop (80%) so it pulls.</summary>
        public static float Natural(ElasticKind kind, float loopLength, int wraps) =>
            !kind.tubing ? kind.NaturalLoop : Mathf.Max(1f, Mathf.Round(loopLength * wraps * 0.8f * 4f) / 4f);

        public static int IndexOf(IList<BandPost> posts, BandPost post, int skip = -1) {
            for (var i = 0; i < posts.Count; i++)
                if (i != skip && posts[i].part == post.part && (posts[i].Center - post.Center).magnitude < Mathf.Max(0.2f, posts[i].radius + 0.05f)) return i;
            return -1;
        }

        /// <summary>Where the mouse holds a band that's being stretched: on the plane it lies in (square to the post
        /// it comes from), or facing the camera.</summary>
        public static BandPost Finger(Camera camera, Ray ray, BandPost from) {
            var center = from.Center;
            var normal = from.HasAxis && !from.hole ? from.Axis : -camera.transform.forward;
            if (Mathf.Abs(Vector3.Dot(ray.direction, normal)) < 0.15f) normal = -camera.transform.forward;
            var plane = new Plane(normal, center);
            var point = plane.Raycast(ray, out var distance) ? ray.GetPoint(distance) : center;
            return new BandPost { part = null, local = point, axisLocal = from.HasAxis && !from.hole ? from.Axis : Vector3.zero, radius = 0.05f, what = "the mouse" };
        }

        public static Color StretchColor(ElasticKind kind, float stretch) =>
            stretch <= 1.02f ? Theme.TextDim : stretch < kind.maxStretch * 0.75f ? Theme.Success : stretch <= kind.maxStretch ? Theme.Warning : Theme.Danger;

        /// <summary>"Stretched 1.6x · pulls 1.2 lbf".</summary>
        public static string Readout(ElasticKind kind, float loopLength, int bands, int wraps, float natural, int postCount) {
            var stretch = loopLength * wraps / natural;
            if (stretch <= 1.02f) return "Slack: it needs a " + Inch.Format(Mathf.Round(natural / wraps * 8f) / 8f) + " loop to stretch";
            var pull = bands * wraps * kind.Tension(stretch);
            var text = "Stretched " + stretch.ToString("0.0", CultureInfo.InvariantCulture) + "x · pulls "
                       + (postCount == 2 ? 2f * pull : pull).ToString("0.0", CultureInfo.InvariantCulture) + " lbf";
            if (stretch > kind.maxStretch) text += "\nToo far: it can snap (keep it under " + kind.maxStretch.ToString("0", CultureInfo.InvariantCulture) + "x)";
            return text;
        }

        public static void DrawLoop(BandShape shape, Color color, float width) {
            for (var i = 0; i < shape.line.Count; i++) Overlay.Line(shape.line[i], shape.line[(i + 1) % shape.line.Count], color, width);
        }

        public static void Ring(Camera camera, BandPost post, Color color) {
            var axis = post.HasAxis && !post.hole ? post.Axis : camera.transform.forward;
            Overlay.WorldCircle(post.Center, axis, Mathf.Max(0.07f, post.radius + 0.03f), color, 2f);
        }

        /// <summary>Whether a point lies inside the loop (seen square to its plane).</summary>
        public static bool Inside(BandShape shape, Vector3 point) {
            var n = shape.normal;
            var u = Vector3.ProjectOnPlane(Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right, n).normalized;
            var v = Vector3.Cross(n, u);
            Vector2 To2(Vector3 w) => new(Vector3.Dot(w, u), Vector3.Dot(w, v));
            var p = To2(point);
            var inside = false;
            for (int i = 0, j = shape.line.Count - 1; i < shape.line.Count; j = i++) {
                var a = To2(shape.line[i]);
                var b = To2(shape.line[j]);
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }
    }

    // V2: the band as it will be, drawn while it's placed or changed (its real strands, color and thickness: not just a
    // line), hidden when there's nothing to show.
    public class BandPreview {
        private GameObject go;
        private Mesh mesh;
        private MeshRenderer renderer;
        private string materialKey;

        public void Show(BandShape shape, ElasticKind kind, int bands, int wraps) {
            if (shape == null || !shape.Valid) {
                Hide();
                return;
            }
            if (go == null) {
                go = new GameObject("Band Preview");
                mesh = new Mesh { name = "Band preview" };
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                renderer = go.AddComponent<MeshRenderer>();
            }
            if (materialKey != kind.key) {
                materialKey = kind.key;
                renderer.sharedMaterial = ElasticPartGenerator.MaterialFor("Band " + kind.key, kind.color, kind.tubing ? 0.45f : 0.25f);
            }
            var origin = shape.line[0];
            go.transform.position = origin;
            ElasticBand.BuildMesh(mesh, shape, kind, bands, wraps, origin);
            go.SetActive(true);
        }

        public void Hide() {
            if (go != null) go.SetActive(false);
        }

        public void Dispose() {
            if (go != null) UnityEngine.Object.Destroy(go);
            if (mesh != null) UnityEngine.Object.Destroy(mesh);
            go = null;
            mesh = null;
        }
    }

    // V2: a selected band changed where it lies (like a geoboard): a dot on each post it's hooked on; drag one onto
    // another post to move that end there, or off into the open to unhook it (3 posts or more); click one (3 or more)
    // to send the band round its other side. A gray dot follows the mouse along the band: drag it onto a post and that
    // strand is hooked round it (pulled out round a post outside the loop, pressed in by one inside it; drag the other
    // strand onto the same post and both go over it). A panel sets which band, how many and the wraps. One undo step
    // per change.
    public class BandEditor : MonoBehaviour {
        private static BandEditor instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Make() {
            if (instance != null) return;
            instance = new GameObject("Band Editor").AddComponent<BandEditor>();
        }

        /// <summary>Selects a band (with the Transform tool) so it can be changed.</summary>
        public static void Select(ElasticBand band) {
            if (band == null) return;
            if (ToolRail.Current != "Transform") ToolRail.Select("Transform");
            var manager = GameObject.Find("Primary Selection Manager")?.GetComponent<SelectionSystem.SelectionManager>();
            manager?.SetCurrent(new SelectionSystem.ObjectSelection { gameObject = band.gameObject });
        }

        /// <summary>The mouse is on one of a selected band's dots (or dragging one).</summary>
        public static bool Busy => instance != null && (instance.band != null && (instance.hook >= 0 || instance.strand >= 0) || instance.hovered);

        private ElasticBand band;
        private int hook = -1, strand = -1;   // what's being dragged: a post's dot, or a strand (the span from post i)
        private Vector2 pressAt;
        private bool hovered;
        private string previewKey;
        private BandShape preview;

        private void Update() {
            hovered = false;
            if (hook < 0 && strand < 0) {
                var part = ToolRail.Current == "Transform" && !Placement.IsPlacing && !Sandbox.Running && !TubeTool.Active && !ElasticTool.Active && !ChainTool.Active
                    ? Tools.SelectTool.SelectedPart : null;
                band = part != null ? part.GetComponent<ElasticBand>() : null;
            }
            if (band == null || band.gameObject.IsDeleted() || band.posts.Count < 2 || band.Shape == null) {
                hook = strand = -1;
                band = null;
                ShowPanel(null);
                return;
            }
            ShowPanel(band);
            var camera = Overlay.ViewCamera;
            if (camera == null) return;
            Overlay.Begin();
            var mouse = Overlay.Mouse;
            var onInterface = hook < 0 && strand < 0 && OverInterface();
            var hover = !onInterface && !OverInterface() && ElasticTool.PostUnderMouse(camera, out var found) ? found : null;

            if (hook >= 0 || strand >= 0) {
                Dragging(camera, camera.ScreenPointToRay(MouseInput.Position), hover, mouse, MouseInput.LeftButton.isPressed);
                return;
            }

            // A post's dot under the mouse, else the nearest point on the band.
            var near = -1;
            var best = onInterface ? 0f : 11f;
            for (var i = 0; i < band.posts.Count; i++)
                if (Overlay.ToScreen(band.posts[i].Center, out var at) && Vector2.Distance(at, mouse) < best) {
                    best = Vector2.Distance(at, mouse);
                    near = i;
                }
            DrawDots(near);
            if (near >= 0) {
                hovered = true;
                var many = band.posts.Count > 2;
                Overlay.MouseText("Drag onto another post to move it there" + (many ? "\nDrag off to unhook it · click: round its other side" : ""), Color.white);
                if (MouseInput.LeftButton.wasPressedThisFrame) {
                    hook = near;
                    pressAt = mouse;
                    previewKey = null;
                }
                return;
            }
            if (onInterface || band.Shape == null || !band.Shape.Valid || !NearestOnLine(band.Shape.line, mouse, out var point, out var screen)) return;
            hovered = true;
            Overlay.Disc(screen, 6f, new Color(0.75f, 0.77f, 0.8f, 1f), true);
            Overlay.MouseText("Drag this strand onto a post to hook it round it", Color.white);
            if (MouseInput.LeftButton.wasPressedThisFrame) {
                strand = NearestSpan(band.Shape, point);
                pressAt = mouse;
                previewKey = null;
            }
        }

        // ------------------------------------------------------------------ dragging an end or a strand

        private void Dragging(Camera camera, Ray ray, BandPost hover, Vector2 mouse, bool held) {
            var posts = band.posts.Select(p => p.Copy()).ToList();
            var moved = (mouse - pressAt).magnitude > 8f;
            BandPost target = null;
            var onPost = false;
            if (hook >= 0) {
                onPost = hover != null && BandUi.IndexOf(posts, hover, hook) < 0 && moved;
                target = onPost ? hover : moved ? BandUi.Finger(camera, ray, posts[hook]) : null;
                if (target != null) {
                    target.inside = posts[hook].inside;
                    posts[hook] = target;
                }
            }
            else {
                // The strand from post `strand` to the next: the post goes between them, on that strand.
                var a = strand;
                var b = (strand + 1) % posts.Count;
                var at = hover != null ? BandUi.IndexOf(posts, hover) : -1;
                onPost = hover != null && moved && at != a && at != b;
                target = onPost ? hover.Copy() : moved ? BandUi.Finger(camera, ray, posts[a]) : null;
                if (target != null) {
                    // Like a band on a geoboard: a strand dragged out to a post outside the loop goes round its far
                    // side; dragged in to one inside the loop it hooks round that post's inner side (a V). Dragged onto a
                    // post the band is already on (the other strand): the band goes round it on both sides, one strand
                    // each side (a roller between the posts).
                    if (at >= 0) {
                        target.inside = false;
                        posts[at].inside = false;
                    }
                    else target.inside = BandUi.Inside(band.Shape, target.Center);
                    posts.Insert(a + 1, target);
                }
            }
            var key = target == null ? "" : string.Join(";", posts.Select(p => p.Center.ToString("F3") + p.inside));
            if (key != previewKey) {
                previewKey = key;
                preview = target != null ? BandUi.Shape(band.kind, posts) : null;
            }
            if (hover != null && onPost) BandUi.Ring(camera, hover, Color.white);
            string label = null;
            var labelColor = Color.white;
            if (preview != null && preview.Valid) {
                var stretch = preview.length * band.wraps / band.Natural;
                labelColor = BandUi.StretchColor(band.kind, stretch);
                // The band as it would be (the band itself steps aside meanwhile).
                look.Show(preview, band.kind, band.bands, band.wraps);
                SetShown(band, false);
                label = BandUi.Readout(band.kind, preview.length, band.bands, band.wraps, band.Natural, posts.Count);
                if (onPost) label += "\nLet go: " + (hook >= 0 ? "move it here" : target.inside ? "press the strand in here" : "pull the strand round this");
                else if (hook >= 0 && band.posts.Count > 2) label += "\nLet go here: unhook it";
            }
            else if (preview != null) {
                label = preview.problem;
                labelColor = Theme.Danger;
            }
            if (label != null) Overlay.MouseText(label, labelColor);
            if (preview == null || !preview.Valid) {
                look.Hide();
                SetShown(band, true);
            }
            DrawDots(hook);
            if (held) return;
            look.Hide();
            SetShown(band, true);

            // Let go.
            var changed = posts;
            if (hook >= 0 && !moved && band.posts.Count > 2) {
                changed = band.posts.Select(p => p.Copy()).ToList();
                changed[hook].inside = !changed[hook].inside;
                Apply(changed);
            }
            else if (hook >= 0 && moved && !onPost && band.posts.Count > 2) {
                changed = band.posts.Select(p => p.Copy()).ToList();
                changed.RemoveAt(hook);
                Apply(changed);
            }
            else if (onPost) Apply(changed);
            hook = strand = -1;
            preview = null;
        }

        private readonly BandPreview look = new();

        private static void SetShown(ElasticBand band, bool shown) {
            if (band != null && band.TryGetComponent(out MeshRenderer r) && r.enabled != shown) r.enabled = shown;
        }

        private void Apply(List<BandPost> posts, ElasticKind kind = null, int bands = -1, int wraps = -1) {
            kind ??= band.kind;
            if (bands < 0) bands = band.bands;
            if (wraps < 0) wraps = band.wraps;
            var shape = BandUi.Shape(kind, posts);
            if (shape == null || !shape.Valid) {
                Interface.Interface.Say(shape?.problem ?? "It can't go round those", false);
                Audio.Sounds.Play(Audio.Sounds.Error);
                return;
            }
            var before = new ElasticBand.LoopElement(band);
            var natural = kind.tubing ? (band.kind.tubing && band.rest > 0f ? band.rest : BandUi.Natural(kind, shape.length, wraps)) : 0f;
            band.UseKind(kind);
            band.Set(posts, bands, wraps, natural, shape.normal);
            StateSystem.AddElement(before);
            StateSystem.AddState(new ElasticBand.LoopElement(band));
            Audio.Sounds.Play(Audio.Sounds.Tick);
            panelKey = null;
        }

        // ------------------------------------------------------------------ the panel

        private ElasticBand panelFor;
        private string panelKey;
        private RectTransform panel;
        private TMPro.TextMeshProUGUI readout;

        private void ShowPanel(ElasticBand shown) {
            var key = shown == null ? null : shown.kind.key + shown.bands + shown.wraps + shown.rest.ToString("F2");
            if (shown == panelFor && key == panelKey && (shown == null || panel != null)) {
                if (readout != null && shown != null && shown.Shape != null && shown.Shape.Valid) {
                    readout.text = BandUi.Readout(shown.kind, shown.Shape.length, shown.bands, shown.wraps, shown.Natural, shown.posts.Count);
                    readout.color = BandUi.StretchColor(shown.kind, shown.Shape.length * shown.wraps / shown.Natural);
                }
                return;
            }
            if (panel != null) Destroy(panel.gameObject);
            panel = null;
            readout = null;
            panelFor = shown;
            panelKey = key;
            if (shown == null || Interface.Interface.Root == null) return;
            panel = BandUi.Bar(Interface.Interface.Root, shown.kind, shown.bands, shown.wraps,
                k => Apply(band.posts.Select(p => p.Copy()).ToList(), k),
                b => Apply(band.posts.Select(p => p.Copy()).ToList(), null, b),
                w => Apply(band.posts.Select(p => p.Copy()).ToList(), null, -1, w),
                () => GameObject.Find("Primary Selection Manager")?.GetComponent<SelectionSystem.SelectionManager>()?.ClearCurrent());
            panel.name = "Band Panel";
            if (shown.kind.tubing) {
                // Tubing is cut to a length and tied: its natural loop.
                const float Max = 48f;
                Dialog.Heading(panel, "Cut length (its loop, unstretched)");
                IElement before = null;
                MotionUi.Slider(panel, Mathf.Clamp01(shown.Natural / Max), 0.02f, 0.25f / Max, f => Inch.Format(Mathf.Round(f * Max * 4f) / 4f), f => {
                    before ??= new ElasticBand.LoopElement(shown);
                    shown.rest = Mathf.Max(1f, Mathf.Round(f * Max * 4f) / 4f);
                }, f => {
                    shown.rest = Mathf.Max(1f, Mathf.Round(f * Max * 4f) / 4f);
                    shown.Set(shown.posts.Select(p => p.Copy()).ToList(), shown.bands, shown.wraps, shown.rest, shown.Shape != null ? shown.Shape.normal : Vector3.up);
                    if (before != null) {
                        StateSystem.AddElement(before);
                        StateSystem.AddState(new ElasticBand.LoopElement(shown));
                    }
                    before = null;
                });
            }
            readout = Ui.Text(panel, "Readout", "", Theme.FontBody, Theme.Text);
            readout.textWrappingMode = TMPro.TextWrappingModes.Normal;
            readout.Size(-1, 40f);
            Ui.Hint(panel, "Drag a dot", "to another post", "Drag the band", "onto a post");
            if (shown.posts.Count > 2) Ui.Hint(panel, "Click a dot", "other side", "Drag a dot off", "unhook it");
            ShowPanel(shown);
        }

        private void OnDestroy() {
            look.Dispose();
            if (panel != null) Destroy(panel.gameObject);
        }

        // ------------------------------------------------------------------ helpers

        private static readonly List<UnityEngine.EventSystems.RaycastResult> uiHits = new();

        // Whether the mouse is on the interface itself (not on the overlay's handles: the band's dots).
        private static bool OverInterface() {
            var events = UnityEngine.EventSystems.EventSystem.current;
            if (events == null) return false;
            uiHits.Clear();
            events.RaycastAll(new UnityEngine.EventSystems.PointerEventData(events) { position = MouseInput.Position }, uiHits);
            foreach (var hit in uiHits)
                if (Overlay.Instance == null || hit.gameObject != Overlay.Instance.gameObject) return true;
            return false;
        }

        private void DrawDots(int lit) {
            for (var i = 0; i < band.posts.Count; i++) {
                if (!Overlay.ToScreen(band.posts[i].Center, out var at)) continue;
                var color = i == lit ? Theme.AccentHover : band.posts[i].inside ? Theme.Warning : Theme.Accent;
                Overlay.Disc(at, i == lit ? 7.5f : 6f, color, true);
                Overlay.Ring(at, 9f, Color.white, 1.5f);
            }
        }

        private static int NearestSpan(BandShape shape, Vector3 point) {
            var best = 0;
            var bestD = float.MaxValue;
            for (var i = 0; i < shape.spans.Count; i++) {
                var (a, b) = shape.spans[i];
                var ab = b - a;
                var t = ab.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude) : 0f;
                var d = (a + ab * t - point).sqrMagnitude;
                if (d < bestD) {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }

        private static bool NearestOnLine(List<Vector3> line, Vector2 mouse, out Vector3 point, out Vector2 screen) {
            point = default;
            screen = default;
            var best = 8f;
            var found = false;
            for (var i = 0; i < line.Count; i++) {
                if (!Overlay.ToScreen(line[i], out var a) || !Overlay.ToScreen(line[(i + 1) % line.Count], out var b)) continue;
                var ab = b - a;
                var s = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(mouse - a, ab) / ab.sqrMagnitude) : 0f;
                var on = a + ab * s;
                var d = Vector2.Distance(on, mouse);
                if (d < best) {
                    best = d;
                    point = Vector3.Lerp(line[i], line[(i + 1) % line.Count], s);
                    screen = on;
                    found = true;
                }
            }
            return found;
        }
    }
}
