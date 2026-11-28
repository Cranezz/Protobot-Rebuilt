using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Protobot.Interface;
using Protobot.StateSystems;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.Motion {
    // V2: putting rubber bands and latex tubing on, like stretching a real one (after geoboard apps, where a band is
    // dragged onto pegs). Pick one in the parts list: the band rides on the mouse. Click a post (a standoff, screw,
    // shaft, spacer, collar, wheel rim, a hole, or any part's edge) to hook it there; it stretches to the mouse as you
    // move, its stretch and pull beside the mouse; click the next post and it's on (or press on the first post and drag
    // to the second). Shift+click hooks more posts on the way, Enter puts it on. A small bar sets which band, how many
    // side by side and how many wraps. Esc lets go, then stops. Once it's on, select it to change it where it lies
    // (BandEditor): drag an end to another post, drag a strand onto a post to hook that strand round it.
    public class ElasticTool : MonoBehaviour {
        private static ElasticTool active;
        public static bool Active => active != null;

        private ElasticKind kind;
        private readonly List<BandPost> picks = new();
        private static int bands = 1, wraps = 1;   // kept for the next band (teams put several on alike)
        private RectTransform bar;
        private bool pressed;          // pressed on a post to hook it: releasing on another puts it on
        private Vector2 pressAt;

        public static void Begin(string key, ElasticBand edit = null) {
            if (Sandbox.Running) return;
            if (edit != null) {
                // Changing a band is done where it lies: select it.
                Stop();
                BandEditor.Select(edit);
                return;
            }
            if (active != null) Destroy(active.gameObject);
            if (Placement.IsPlacing) Placement.Instance.StopPlacing();
            ChainTool.Stop();
            TubeTool.Stop();
            StringTool.Stop();   // V2 (build 88)
            foreach (var manager in FindObjectsByType<SelectionSystem.SelectionManager>(FindObjectsSortMode.None)) manager.ClearCurrent();
            active = Ui.Rect("Elastic Tool", Interface.Interface.Root).Fill().gameObject.AddComponent<ElasticTool>();
            active.kind = ElasticKind.Of(key);
            active.Build();
        }

        public static void Stop() {
            if (active == null) return;
            Destroy(active.gameObject);
            active = null;
        }

        private readonly BandPreview look = new();

        private void OnDestroy() {
            look.Dispose();
            ToolBlock.Pop();
            if (active == this) active = null;
        }

        // ------------------------------------------------------------------ the bar

        private void Build() {
            ToolBlock.Push();
            Rebuild();
        }

        private void Rebuild() {
            if (bar != null) Destroy(bar.gameObject);
            bar = BandUi.Bar(transform, kind, bands, wraps, k => {
                kind = k;
                Rebuild();
            }, b => {
                bands = b;
                Rebuild();
            }, w => {
                wraps = w;
                Rebuild();
            }, Stop);
            Ui.Hint(bar, "Click", picks.Count == 0 ? "a post to hook it on" : "the next post", "Shift+click", "more posts", "Esc", picks.Count > 0 ? "let go" : "done");
        }

        // ------------------------------------------------------------------ putting it on

        private void Make() {
            var shape = BandUi.Shape(kind, picks);
            if (picks.Count < 2 || shape == null || !shape.Valid) {
                Interface.Interface.Say(shape?.problem ?? "Hook it on at least two things", false);
                Audio.Sounds.Play(Audio.Sounds.Error);
                return;
            }
            var natural = BandUi.Natural(kind, shape.length, wraps);
            var type = PartsManager.GetPartType("BAND");
            var generator = type.GetComponent<PartGenerator>();
            generator.param1.value = kind.key;
            var obj = generator.Generate(picks[0].Center, Quaternion.identity);
            var band = obj.GetComponent<ElasticBand>();
            band.Set(picks, bands, wraps, kind.tubing ? natural : 0f, shape.normal);
            StateSystem.AddElement(new ObjectElement(obj) { existing = false });
            StateSystem.AddState(new State(new List<IElement> { new ObjectElement(obj), new ElasticBand.LoopElement(band) }));
            Audio.Sounds.Play(Audio.Sounds.Clack);
            // Ready for the next one.
            picks.Clear();
            Rebuild();
        }

        // ------------------------------------------------------------------ every frame

        private void Update() {
            var keyboard = Keyboard.current;
            if (Sandbox.Running) {
                Stop();
                return;
            }
            if (keyboard != null && !InputEvents.TextInput.IsTyping && !InputEvents.TextInput.ShortcutsPaused) {
                if (keyboard.escapeKey.wasPressedThisFrame) {
                    if (picks.Count > 0) {
                        picks.Clear();
                        pressed = false;
                        Rebuild();
                    }
                    else Stop();
                    return;
                }
                if ((keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) && picks.Count >= 2) {
                    Make();
                    return;
                }
                if (keyboard.backspaceKey.wasPressedThisFrame && picks.Count > 0) {
                    picks.RemoveAt(picks.Count - 1);
                    Rebuild();
                }
            }
            var camera = Overlay.ViewCamera;
            if (camera == null) return;
            var hover = !MouseInput.overUI && PostUnderMouse(camera, out var found) ? found : null;
            var shift = keyboard != null && keyboard.shiftKey.isPressed;
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && hover != null) Press(hover, shift);
            else if (mouse != null && mouse.leftButton.wasReleasedThisFrame && pressed) {
                pressed = false;
                // Pressed on the first post and dragged to another: it's on.
                if (hover != null && BandUi.IndexOf(picks, hover) < 0 && (Overlay.Mouse - pressAt).magnitude > 8f) {
                    picks.Add(hover);
                    Audio.Sounds.Play(Audio.Sounds.Tick);
                    if (!shift) Make();
                    else Rebuild();
                }
            }
            Draw(camera, hover, shift);
        }

        private void Press(BandPost post, bool shift) {
            var index = BandUi.IndexOf(picks, post);
            if (picks.Count == 0) {
                picks.Add(post);
                pressed = true;
                pressAt = Overlay.Mouse;
                Audio.Sounds.Play(Audio.Sounds.Tick);
                Rebuild();
                return;
            }
            if (index >= 0) {
                // Back on the first post with more hooked: put it on. Another one hooked: the band goes round its
                // other side.
                if (index == 0 && picks.Count >= 2) Make();
                else if (index > 0) {
                    picks[index].inside = !picks[index].inside;
                    shapeKey = null;
                    Audio.Sounds.Play(Audio.Sounds.Tick);
                }
                return;
            }
            picks.Add(post);
            Audio.Sounds.Play(Audio.Sounds.Tick);
            if (!shift) Make();
            else Rebuild();
        }

        private string shapeKey;
        private BandShape shown;

        private void Draw(Camera camera, BandPost hover, bool shift) {
            Overlay.Begin();
            var mouse = Overlay.Mouse;
            foreach (var p in picks) BandUi.Ring(camera, p, Theme.Accent);
            if (picks.Count == 0) {
                look.Hide();
                // The band on the mouse, in its color.
                Overlay.Ring(mouse + new Vector2(16f, -14f), 7f, kind.color, 3f);
                if (hover != null) {
                    BandUi.Ring(camera, hover, Color.white);
                    Overlay.MouseText("Hook it on: " + (hover.what ?? "post"), Color.white);
                }
                return;
            }
            // Stretched from what it's hooked on to the post under the mouse, or to the mouse itself.
            var onPost = hover != null && BandUi.IndexOf(picks, hover) < 0;
            var to = onPost ? hover : BandUi.Finger(camera, camera.ScreenPointToRay(MouseInput.Position), picks[^1]);
            var list = new List<BandPost>(picks) { to };
            var key = string.Join(";", list.Select(p => p.Center.ToString("F3") + p.inside)) + kind.key + bands + wraps;
            if (key != shapeKey) {
                shapeKey = key;
                shown = BandUi.Shape(kind, list);
            }
            if (onPost) BandUi.Ring(camera, hover, Color.white);
            if (shown == null || !shown.Valid) {
                look.Hide();
                Overlay.MouseText(shown?.problem ?? "", Theme.Danger);
                return;
            }
            var natural = BandUi.Natural(kind, shown.length, wraps);
            var color = BandUi.StretchColor(kind, shown.length * wraps / natural);
            look.Show(shown, kind, bands, wraps);   // the band itself, as it would be
            var label = BandUi.Readout(kind, shown.length, bands, wraps, natural, list.Count);
            if (onPost) label += "\n" + (shift ? "Shift+click: hook it here too" : "Click: put it on");
            Overlay.MouseText(label, color);
        }

        // ------------------------------------------------------------------ what's under the mouse

        /// <summary>What a band would go round under the mouse: a hole (through it), a post-like part (a screw,
        /// standoff, shaft, or anything round on an axle: its radius where the mouse is), else the point on the part
        /// (round its edge).</summary>
        public static bool PostUnderMouse(Camera camera, out BandPost post) => PostAt(camera.ScreenPointToRay(MouseInput.Position), out post);

        /// <summary>What a band would go round along a ray (PostUnderMouse).</summary>
        public static bool PostAt(Ray ray, out BandPost post) {
            post = null;
            var hits = Physics.RaycastAll(ray, Mathf.Infinity, LayerMask.GetMask("Default", "HoleCollisions"), QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits) {
                if (hit.collider.TryGetComponent(out HoleCollider hole)) {
                    var owner = hole.transform.parent != null ? hole.transform.parent.gameObject : null;
                    if (owner == null || owner.IsDeleted() || owner.GetComponent<SavedObject>() == null) continue;
                    // A hole with something in it: the band goes round what's in it.
                    if (!hole.IsEmpty) continue;
                    post = new BandPost {
                        part = owner,
                        local = owner.transform.InverseTransformPoint(hole.transform.position),
                        axisLocal = owner.transform.InverseTransformDirection(hole.transform.forward),
                        radius = 0.02f, hole = true, what = "Through a hole in the " + PartLabels.Short(owner),
                    };
                    return true;
                }
                var saved = hit.collider.GetComponentInParent<SavedObject>();
                if (saved == null || saved.gameObject.IsDeleted()) continue;
                var part = SelectAs.Resolve(hit.collider.gameObject) ?? saved.gameObject;
                if (part.GetComponent<ElasticBand>() != null || part.GetComponent<PneumaticTube>() != null) continue;
                if (PostLine(part, out var origin, out var axis)) {
                    var t = part.transform;
                    var originLocal = t.InverseTransformPoint(origin);
                    var axisLocal = t.InverseTransformDirection(axis).normalized;
                    var along = Vector3.Dot(t.InverseTransformPoint(hit.point) - originLocal, axisLocal);
                    var radius = Strands.RadiusAt(part, originLocal, axisLocal, along);
                    post = new BandPost {
                        part = part, local = originLocal + axisLocal * along, axisLocal = axisLocal,
                        radius = Mathf.Clamp(radius, 0.03f, 3f), what = PartLabels.Short(part),
                    };
                    return true;
                }
                post = new BandPost {
                    part = part, local = part.transform.InverseTransformPoint(hit.point), radius = 0f,
                    what = "Round the " + PartLabels.Short(part) + "'s edge",
                };
                return true;
            }
            return false;
        }

        /// <summary>A post's line: a screw's or fitting's shank, a standoff's or shaft's length, a round part's axle.</summary>
        public static bool PostLine(GameObject part, out Vector3 origin, out Vector3 axis) {
            origin = default;
            axis = default;
            var t = part.transform;
            var id = part.GetComponent<SavedObject>()?.id ?? "";
            if (part.GetComponent<Screw>() != null) {
                origin = t.position;
                axis = t.forward;
                return true;
            }
            if (id.StartsWith("SNDF") || id.StartsWith("SHFT") || id.Contains("Standoff") || id.Contains("Shaft")) {
                origin = part.TryGetComponent(out Renderer r) ? r.bounds.center : t.position;
                axis = t.TransformDirection(Tools.PartBend.AlongLocal(part)).normalized;
                return true;
            }
            if (AxlePlaceDisplacement.AxleOf(part, out var axle)) {
                origin = t.TransformPoint(axle.center);
                axis = t.TransformDirection(axle.axis).normalized;
                return true;
            }
            return false;
        }
    }
}
