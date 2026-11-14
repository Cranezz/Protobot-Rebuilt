using Protobot.Interface;
using Protobot.Tools;
using UnityEngine;

namespace Protobot.Motion {
    // V2: sliding a cylinder's rod in the builder: a selected cylinder (Transform tool) shows an arrow at its rod's
    // end; drag it along the rod to set the rod anywhere in its stroke (1/16" steps, Shift free), never past either
    // end, and what's on the rod moves with it (one undo step). It snaps like the Transform tool (its Snap switch and
    // Move step; Shift: free). Right-click > Rod position and air… types it.
    public class RodSlider : MonoBehaviour {
        private static RodSlider instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Make() {
            if (instance != null) return;
            instance = new GameObject("Rod Slider").AddComponent<RodSlider>();
        }

        private PneumaticCylinder dragging;
        private Vector3 lineStart, lineDirection;
        private float startPosition;
        private Vector2 pressAt;

        /// <summary>The mouse is on the rod's arrow (or dragging it).</summary>
        public static bool Busy => instance != null && (instance.dragging != null || instance.hovered);
        private bool hovered;

        private void Update() {
            hovered = false;
            var cylinder = dragging;
            if (cylinder == null) {
                var part = ToolRail.Current == "Transform" && !Placement.IsPlacing && !Sandbox.Running ? SelectTool.SelectedPart : null;
                cylinder = part != null ? part.GetComponent<PneumaticCylinder>() : null;
            }
            if (cylinder == null || !cylinder.enabled || cylinder.gameObject.IsDeleted() || cylinder.Rod == null || Sandbox.Running) {
                Stop();
                return;
            }
            var camera = Overlay.ViewCamera;
            if (camera == null) return;
            var tip = cylinder.RodTip;
            var direction = cylinder.ExtendDirection;
            // An arrow pointing straight at the viewer can't be dragged: leave it out.
            if (dragging == null && Mathf.Abs(Vector3.Dot(direction, camera.transform.forward)) > 0.97f) return;
            var perPixel = MirrorTool.PerPixel(camera, tip);
            var handle = tip + direction * (44f * perPixel);
            Overlay.Begin();
            if (!Overlay.ToScreen(tip, out var tipAt) || !Overlay.ToScreen(handle, out var at)) return;
            var mouse = Overlay.Mouse;
            var screenDir = (at - tipAt).sqrMagnitude > 1f ? (at - tipAt).normalized : Vector2.right;
            // The arrow is picked anywhere along it, like the Transform tool's arrows.
            hovered = dragging == null && Overlay.DistanceToSegment(mouse, tipAt + screenDir * 6f, at + screenDir * 12f) < 9f;
            var lit = hovered || dragging != null;
            var color = lit ? Theme.AccentHover : Theme.Accent;

            // The rod's travel: a track from all the way in to all the way out, with a tick per snap step.
            var stroke = cylinder.Stroke;
            var inPoint = tip - direction * (cylinder.Position * stroke);
            var outPoint = inPoint + direction * stroke;
            if (lit && Overlay.ToScreen(inPoint, out var inAt) && Overlay.ToScreen(outPoint, out var outAt)) {
                var track = new Color(1f, 1f, 1f, 0.35f);
                var side = new Vector2(-screenDir.y, screenDir.x);
                Overlay.Line(inAt, outAt, track, 2f);
                Overlay.Line(inAt - side * 7f, inAt + side * 7f, track, 2f);
                Overlay.Line(outAt - side * 7f, outAt + side * 7f, track, 2f);
                var step = Step;
                if (step > 0f && stroke / step <= 64f && (outAt - inAt).magnitude / Mathf.Max(1f, stroke / step) > 5f)
                    for (var d = step; d < stroke - 0.001f; d += step)
                        if (Overlay.ToScreen(inPoint + direction * d, out var tick))
                            Overlay.Line(tick - side * 3.5f, tick + side * 3.5f, track, 1.5f);
            }
            // A gizmo-style arrow out of the rod's end: shaft and head, both ways (it slides in and out).
            Overlay.Line(tipAt + screenDir * 6f, at, color, lit ? 3.5f : 3f);
            Overlay.Arrowhead(at + screenDir * 14f, at, color, 14f, 12f);
            Overlay.Disc(tipAt, lit ? 5.5f : 4.5f, color);

            if (dragging != null) {
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                var along = MirrorTool.AlongLine(camera, lineStart, lineDirection);
                var inches = startPosition * stroke + along;
                // The Transform tool's snapping: its Move step when Snap is on (Shift: free for this drag).
                var snap = Step;
                if (snap > 0f && (keyboard == null || !keyboard.shiftKey.isPressed)) inches = Mathf.Round(inches / snap) * snap;
                inches = Mathf.Clamp(inches, 0f, stroke);
                if (Vector2.Distance(mouse, pressAt) > 1f) cylinder.ManualTo(inches / Mathf.Max(0.001f, stroke));
                var end = cylinder.Position < 0.0005f ? " (all the way in)" : cylinder.Position > 0.9995f ? " (all the way out)" : "";
                Overlay.MouseText(Inch.Format(cylinder.Position * stroke) + " out of " + Inch.Format(stroke) + end, Theme.Accent, null, true);
                if (!MouseInput.LeftButton.isPressed) {
                    cylinder.EndManual();
                    dragging = null;
                }
                return;
            }
            if (hovered) {
                Overlay.MouseText("Drag to slide the rod" + (Step > 0f ? " (snaps " + Inch.Format(Step) + ", Shift: free)" : ""), Color.white);
                if (MouseInput.LeftButton.wasPressedThisFrame && cylinder.BeginManual()) {
                    dragging = cylinder;
                    lineStart = cylinder.RodTip;
                    lineDirection = direction;
                    startPosition = cylinder.Position;
                    pressAt = mouse;
                }
            }
        }

        // The Transform tool's move snap (0 when its Snap switch is off).
        public static float Step => PositionTool.snapping ? TransformGizmo.MoveStep : 0f;

        private void Stop() {
            if (dragging != null) dragging.EndManual();
            dragging = null;
        }
    }
}
