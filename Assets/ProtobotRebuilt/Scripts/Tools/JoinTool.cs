using System.Collections.Generic;
using System.Linq;
using Protobot.Interface;
using Protobot.InputEvents;
using Protobot.StateSystems;
using Protobot.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Tools {
    // V2: the join tool (key 7), like mating in a CAD program. Click a face, edge, corner or hole on the part to
    // move, then one on the part to put it against: the first part (with everything screwed to it) moves so the
    // two meet.
    // - Face to face (or hole face to hole face): the faces turn to face each other and the clicked points meet;
    //   the part is then squared up with the other part's sides (when it's within 15° of that).
    // - Edge to edge: the edges line up (the shorter turn) and the points meet.
    // - Anything with a corner or a point: the points meet, without turning.
    // After joining, Flip turns it over the other way, and the arrows turn it 90° about the joined axis. Undo
    // puts it back. Esc lets go of the first pick, then switches to the Transform tool.
    public class JoinTool : MonoBehaviour {
        [SerializeField] private FeedbackDisplay feedback;

        private FeaturePicker.Feature first;
        private List<GameObject> moving;
        private static JoinTool active;

        // The last join, so Flip and the 90° turns can redo it differently.
        private FeaturePicker.Feature lastFirst, lastSecond;
        private List<GameObject> lastMoving;
        private List<(Vector3 position, Quaternion rotation)> lastStart;
        private bool flipped;
        private int quarterTurns;

        // Animating the move.
        private readonly List<(Transform t, Vector3 fromPos, Quaternion fromRot, Vector3 toPos, Quaternion toRot)> tweens = new();
        private float tweenTime = -1f;
        private const float TweenDuration = 0.2f;

        [RuntimeInitializeOnLoadMethod]
        private static void RegisterOptions() => ToolRail.RegisterOptions("Join", BuildOptions);

        private static void BuildOptions(RectTransform bar) {
            if (active == null) return;
            if (active.first.Valid) {
                ToolOptions.Note(bar, "Moving " + PartLabels.Short(active.first.part) + " from its " + active.first.Describe().ToLowerInvariant());
                ToolOptions.Note(bar, "Now click where it goes").color = Theme.Text;
                Ui.Hint(bar, "Esc", "let go");
                return;
            }
            ToolOptions.Note(bar, "Click a face, edge, corner or hole on the part to move");
            if (active.lastMoving != null) {
                Ui.Divider(bar, true).Size(1f, 22f);
                var flip = Ui.Button(bar, "Flip", "Flip", () => active.Redo(!active.flipped, active.quarterTurns), false, 5f, Theme.FontSmall);
                flip.Size(-1f, 30f);
                Ui.Tip(flip, "Flip", "F", "Turn the joined part over to the other side of the face or edge.");
                var left = Ui.Button(bar, "Turn Left", "-90°", () => active.Redo(active.flipped, active.quarterTurns - 1), false, 5f, Theme.FontSmall);
                left.Size(-1f, 30f);
                Ui.Tip(left, "Turn -90°", null, "Turn the joined part a quarter turn about the joined axis.");
                var right = Ui.Button(bar, "Turn Right", "+90°", () => active.Redo(active.flipped, active.quarterTurns + 1), false, 5f, Theme.FontSmall);
                right.Size(-1f, 30f);
                Ui.Tip(right, "Turn +90°", null, "Turn the joined part a quarter turn about the joined axis.");
            }
            Ui.Hint(bar, "Esc", "Transform tool");
        }

        private void OnEnable() {
            active = this;
            first = default;
            moving = null;
            lastMoving = null;
            ToolOptions.Rebuild();
        }

        private void OnDisable() {
            FinishTween();
            Outline(moving, false);
            if (active == this) active = null;
        }

        private void Update() {
            Overlay.Begin();
            UpdateTween();
            var keyboard = Keyboard.current;
            if (!TextInput.IsTyping && !TextInput.ShortcutsPaused && keyboard != null) {
                if (keyboard.escapeKey.wasPressedThisFrame) {
                    if (first.Valid) {
                        Release();
                        feedback.Show("Let go of the part", FeedbackDisplay.DisplayType.alert);
                    }
                    else ToolRail.Select("Transform");
                    return;
                }
                if (keyboard.fKey.wasPressedThisFrame && lastMoving != null && !first.Valid)
                    Redo(!flipped, quarterTurns);
            }

            var hover = MouseInput.withinScreen && !MouseInput.overUI ? FeaturePicker.Pick() : default;
            // The second pick can't be on the part that's moving.
            if (first.Valid && hover.Valid && moving.Contains(hover.part))
                hover = default;
            FeaturePicker.DrawHover(hover, Theme.Accent);
            if (first.Valid) {
                FeaturePicker.Draw(first, Theme.Dimension);
                if (hover.Valid && Overlay.ToScreen(first.point, out var a) && Overlay.ToScreen(hover.point, out var b)) {
                    Overlay.Line(a, b, Theme.Dimension, 1.5f);
                    Overlay.Arrowhead(b, a, Theme.Dimension);
                }
            }

            if (!MouseInput.LeftButton.wasPressedThisFrame || MouseInput.overUI || !MouseInput.withinScreen)
                return;
            var clicked = FeaturePicker.Pick();
            if (!first.Valid) {
                if (!clicked.Valid) {
                    feedback.Show("Click a face, edge, corner or hole on a part", FeedbackDisplay.DisplayType.alert);
                    return;
                }
                first = clicked;
                moving = clicked.part.GetConnectedObjects(true).Where(o => o.GetComponent<SavedObject>() != null).ToList();
                if (!moving.Contains(clicked.part)) moving.Add(clicked.part);
                Outline(moving, true);
                ToolOptions.Rebuild();
                return;
            }
            if (!clicked.Valid) {
                feedback.Show("Click a face, edge, corner or hole on the part to join it to", FeedbackDisplay.DisplayType.alert);
                return;
            }
            if (moving.Contains(clicked.part)) {
                feedback.Show("Pick a point on another part (this one is moving)", FeedbackDisplay.DisplayType.alert);
                return;
            }
            Join(first, clicked, moving);
            Release();
        }

        private void Release() {
            Outline(moving, false);
            first = default;
            moving = null;
            ToolOptions.Rebuild();
        }

        private static void Outline(List<GameObject> objects, bool on) {
            if (objects == null) return;
            foreach (var obj in objects)
                if (obj != null && obj.TryGetComponent(out MeshRenderer renderer))
                    Rendering.OutlineFeature.SetOutline(renderer, on, Theme.Accent);
        }

        // ------------------------------------------------------------------ joining

        private void Join(FeaturePicker.Feature a, FeaturePicker.Feature b, List<GameObject> objects) {
            FinishTween();
            lastFirst = a;
            lastSecond = b;
            lastMoving = objects;
            lastStart = objects.Select(o => (o.transform.position, o.transform.rotation)).ToList();
            flipped = false;
            quarterTurns = 0;

            // Undo: the current state learns where they were.
            ObjectElement.AddObjectElements(objects);
            Apply(true);
            feedback.Show("Joined " + PartLabels.Short(a.part) + " " + How(a, b) + " " + PartLabels.Short(b.part), FeedbackDisplay.DisplayType.success);
        }

        private static string How(FeaturePicker.Feature a, FeaturePicker.Feature b) {
            if (IsFace(a) && IsFace(b)) return "face to face with";
            if (a.HasDirection && b.HasDirection) return "edge to edge with";
            return "to";
        }

        /// <summary>Joins the last pair again, flipped or turned (from where the parts were before).</summary>
        private void Redo(bool flip, int turns) {
            if (lastMoving == null || lastMoving.Any(o => o == null)) return;
            FinishTween();
            for (var i = 0; i < lastMoving.Count; i++)
                lastMoving[i].transform.SetPositionAndRotation(lastStart[i].position, lastStart[i].rotation);
            flipped = flip;
            quarterTurns = ((turns % 4) + 4) % 4;
            Apply(false);
            feedback.Show(flip ? "Flipped" : "Turned", FeedbackDisplay.DisplayType.success);
        }

        private void Apply(bool newState) {
            var a = lastFirst;
            var b = lastSecond;
            // The first feature where it is now (the parts are back at their start).
            var rotation = Rotation(a, b, out var axis);
            if (quarterTurns != 0 && axis.sqrMagnitude > 0.5f)
                rotation = Quaternion.AngleAxis(90f * quarterTurns, axis) * rotation;
            var pivot = a.point;
            var targets = new List<(Transform, Vector3, Quaternion, Vector3, Quaternion)>();
            foreach (var obj in lastMoving) {
                var t = obj.transform;
                var position = b.point + rotation * (t.position - pivot);
                targets.Add((t, t.position, t.rotation, position, rotation * t.rotation));
            }
            StartTween(targets);
            // The new state: where they are after the join (set now, the tween only animates there).
            if (newState) {
                foreach (var (t, _, _, toPos, toRot) in targets) t.SetPositionAndRotation(toPos, toRot);
                StateSystem.AddEmptyState();
                ObjectElement.AddObjectElements(lastMoving);
                foreach (var (t, fromPos, fromRot, _, _) in targets) t.SetPositionAndRotation(fromPos, fromRot);
            }
            else {
                foreach (var (t, _, _, toPos, toRot) in targets) t.SetPositionAndRotation(toPos, toRot);
                StateSystem.curState.elements.RemoveAll(e => e is ObjectElement oe && lastMoving.Contains(oe.obj));
                ObjectElement.AddObjectElements(lastMoving);
                foreach (var (t, fromPos, fromRot, _, _) in targets) t.SetPositionAndRotation(fromPos, fromRot);
            }
            TransformGizmo.MarkDirty();
        }

        private static bool IsFace(FeaturePicker.Feature f) =>
            f.kind == FeaturePicker.Kind.Face || f.kind == FeaturePicker.Kind.HoleFace || f.kind == FeaturePicker.Kind.Hole;

        // How the moving part turns, and the axis the join leaves free to turn about.
        private Quaternion Rotation(FeaturePicker.Feature a, FeaturePicker.Feature b, out Vector3 axis) {
            axis = Vector3.zero;
            if (IsFace(a) && IsFace(b)) {
                var target = flipped ? b.normal : -b.normal;
                var turn = Quaternion.FromToRotation(a.normal, target);
                axis = b.normal.normalized;
                return SquareUp(turn, a.part, b.part, axis) ;
            }
            if (a.HasDirection && b.HasDirection) {
                var direction = Vector3.Dot(a.direction, b.direction) >= 0f ? b.direction : -b.direction;
                if (flipped) direction = -direction;
                axis = b.direction.normalized;
                return SquareUp(Quaternion.FromToRotation(a.direction, direction), a.part, b.part, axis);
            }
            if (flipped && a.normal.sqrMagnitude > 0.5f && b.normal.sqrMagnitude > 0.5f) {
                axis = b.normal.normalized;
                return Quaternion.FromToRotation(a.normal, b.normal);
            }
            return Quaternion.identity;
        }

        // After the main turn, a small extra turn about the axis so the moving part's sides line up with the other
        // part's (when they're already within 15°): joined channels end up square to each other.
        private static Quaternion SquareUp(Quaternion turn, GameObject movingPart, GameObject targetPart, Vector3 axis) {
            var movingAxes = new[] { movingPart.transform.right, movingPart.transform.up, movingPart.transform.forward };
            var targetAxes = new[] { targetPart.transform.right, targetPart.transform.up, targetPart.transform.forward };
            var best = 0f;
            var bestAbs = 15f;
            foreach (var m in movingAxes) {
                var turned = Vector3.ProjectOnPlane(turn * m, axis);
                if (turned.sqrMagnitude < 0.25f) continue;
                foreach (var t in targetAxes) {
                    var flat = Vector3.ProjectOnPlane(t, axis);
                    if (flat.sqrMagnitude < 0.25f) continue;
                    var angle = Vector3.SignedAngle(turned, flat, axis);
                    // Square to the side either way round (and at 90°).
                    var snapped = angle - Mathf.Round(angle / 90f) * 90f;
                    if (Mathf.Abs(snapped) < bestAbs) {
                        bestAbs = Mathf.Abs(snapped);
                        best = snapped;
                    }
                }
            }
            return Quaternion.AngleAxis(best, axis) * turn;
        }

        // ------------------------------------------------------------------ animation

        private void StartTween(List<(Transform, Vector3, Quaternion, Vector3, Quaternion)> targets) {
            tweens.Clear();
            tweens.AddRange(targets);
            tweenTime = 0f;
        }

        private void UpdateTween() {
            if (tweenTime < 0f) return;
            tweenTime += Time.unscaledDeltaTime;
            var k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(tweenTime / TweenDuration));
            foreach (var (t, fromPos, fromRot, toPos, toRot) in tweens)
                if (t != null) t.SetPositionAndRotation(Vector3.Lerp(fromPos, toPos, k), Quaternion.Slerp(fromRot, toRot, k));
            if (tweenTime >= TweenDuration) {
                tweenTime = -1f;
                tweens.Clear();
                TransformGizmo.MarkDirty();
                ToolOptions.Rebuild();
            }
        }

        private void FinishTween() {
            if (tweenTime < 0f) return;
            tweenTime = TweenDuration;
            UpdateTween();
        }
    }
}
