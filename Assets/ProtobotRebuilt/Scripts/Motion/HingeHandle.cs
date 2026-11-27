using Protobot.Interface;
using Protobot.Tools;
using UnityEngine;

namespace Protobot.Motion {
    // V2: turning a hinge by hand (the user's ask: it should turn like a hinge). A selected hinge (Transform tool)
    // shows a ring round its pin through its top leaf; drag along it and the leaf swings round the pin with the mouse
    // (0° shut to 270°, 5° steps, Shift free), carrying what's screwed to it. One undo step. Right-click > Hinge angle…
    // types it.
    public class HingeHandle : MonoBehaviour {
        private static HingeHandle instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Make() {
            if (instance != null) return;
            instance = new GameObject("Hinge Handle").AddComponent<HingeHandle>();
        }

        private HingePart dragging;
        private float lastScreenAngle, wanted;
        private bool hovered;

        public static bool Busy => instance != null && (instance.dragging != null || instance.hovered);

        private void Update() {
            hovered = false;
            var hinge = dragging;
            if (hinge == null) {
                var part = ToolRail.Current == "Transform" && !Placement.IsPlacing && !Sandbox.Running ? SelectTool.SelectedPart : null;
                hinge = part != null ? part.GetComponent<HingePart>() : null;
            }
            if (hinge == null || !hinge.enabled || hinge.Leaf == null || hinge.gameObject.IsDeleted() || Sandbox.Running) {
                Stop();
                return;
            }
            var camera = Overlay.ViewCamera;
            if (camera == null) return;
            Overlay.Begin();
            var axis = hinge.PinAxis;
            var leafRenderer = hinge.Leaf.GetComponent<Renderer>();
            var leafMiddle = leafRenderer != null ? leafRenderer.bounds.center : hinge.Leaf.position;
            var center = hinge.PinPoint + axis * Vector3.Dot(leafMiddle - hinge.PinPoint, axis);
            var radius = Mathf.Max(0.5f, Vector3.ProjectOnPlane(leafMiddle - hinge.PinPoint, axis).magnitude * 1.6f);
            var u = Vector3.Cross(axis, Mathf.Abs(Vector3.Dot(axis, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var v = Vector3.Cross(axis, u);
            if (!Overlay.ToScreen(center, out var c) || !Overlay.ToScreen(center + u * radius, out var pu) || !Overlay.ToScreen(center + v * radius, out var pv)) return;
            var sense = (pu.x - c.x) * (pv.y - c.y) - (pu.y - c.y) * (pv.x - c.x) >= 0f ? 1f : -1f;
            // Seen edge-on the ring can't be dragged round: leave it out.
            if (dragging == null && Mathf.Abs(Vector3.Dot(axis, camera.transform.forward)) < 0.15f) return;
            const int Segments = 48;
            var points = new Vector2[Segments + 1];
            for (var i = 0; i <= Segments; i++) {
                var a = i * Mathf.PI * 2f / Segments;
                if (!Overlay.ToScreen(center + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius, out points[i])) return;
            }
            var mouse = Overlay.Mouse;
            var near = float.MaxValue;
            for (var i = 0; i < Segments; i++) near = Mathf.Min(near, Overlay.DistanceToSegment(mouse, points[i], points[i + 1]));
            hovered = dragging == null && near < 9f;
            var lit = hovered || dragging != null;
            var color = lit ? Theme.AccentHover : Theme.Accent;
            for (var i = 0; i < Segments; i++) Overlay.Line(points[i], points[i + 1], color, lit ? 3.5f : 2.5f);
            foreach (var k in new[] { 6, 30 }) Overlay.Arrowhead(points[k + 2], points[k], color, 12f, 10f);
            foreach (var k in new[] { 18, 42 }) Overlay.Arrowhead(points[k - 2], points[k], color, 12f, 10f);

            var screenAngle = Mathf.Atan2(mouse.y - c.y, mouse.x - c.x) * Mathf.Rad2Deg;
            if (dragging != null) {
                wanted += Mathf.DeltaAngle(lastScreenAngle, screenAngle) * sense;
                lastScreenAngle = screenAngle;
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                var to = keyboard != null && keyboard.shiftKey.isPressed ? wanted : Mathf.Round(wanted / 5f) * 5f;
                to = Mathf.Clamp(to, HingePart.MinAngle, HingePart.MaxAngle);
                if (Mathf.Abs(to - hinge.Angle) > 0.01f) hinge.ManualTo(to);
                Overlay.MouseText(hinge.Angle.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "° between the leaves (Shift: free)", Theme.Accent, null, true);
                if (!MouseInput.LeftButton.isPressed) Stop();
                return;
            }
            if (hovered) {
                Overlay.MouseText("Drag round to swing the hinge", Color.white);
                if (MouseInput.LeftButton.wasPressedThisFrame && hinge.BeginManual()) {
                    dragging = hinge;
                    wanted = hinge.Angle;
                    lastScreenAngle = screenAngle;
                }
            }
        }

        private void Stop() {
            if (dragging != null) dragging.EndManual();
            dragging = null;
        }
    }
}
