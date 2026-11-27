using System.Linq;
using Protobot.Interface;
using Protobot.Tools;
using UnityEngine;

namespace Protobot.Motion {
    // V2: turning a hand crank in the builder (the user's ask: the crank turns its shaft and what that drives). A
    // selected hand crank (Transform tool) shows a ring round its shaft through its handle; drag along the ring and
    // the crank turns with the mouse, and everything geared, chained or screwed to its shaft with it (racks and nuts
    // slide, followers ride their cams, a ratchet only lets it go one way). Letting go is one undo step.
    public class CrankHandle : MonoBehaviour {
        private static CrankHandle instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Make() {
            if (instance != null) return;
            instance = new GameObject("Crank Handle").AddComponent<CrankHandle>();
        }

        private GameObject dragging;
        private MotorRunner.Run run;
        private Rotor rotor;
        private float lastScreenAngle, turned;
        private bool hovered;

        /// <summary>The mouse is on a crank's ring (or turning it).</summary>
        public static bool Busy => instance != null && (instance.dragging != null || instance.hovered);

        private void Update() {
            hovered = false;
            var crank = dragging;
            if (crank == null) {
                var part = ToolRail.Current == "Transform" && !Placement.IsPlacing && !Sandbox.Running ? SelectTool.SelectedPart : null;
                crank = part != null && GearKit.Kind(part) == KitKind.Crank ? part : null;
            }
            if (crank == null || crank.IsDeleted() || Sandbox.Running) {
                Stop();
                return;
            }
            var camera = Overlay.ViewCamera;
            if (camera == null) return;
            var r = dragging != null ? rotor : GearTrain.Current().FirstOrDefault(x => x.parts.Contains(crank));
            Overlay.Begin();
            if (r == null) {
                if (crank.TryGetComponent(out Renderer lone) && Overlay.ToScreen(lone.bounds.center, out _))
                    Overlay.MouseText("Put the crank on a shaft to turn it", Theme.TextDim);
                return;
            }
            // The ring: round the shaft, through the crank's handle.
            var shape = GearKit.Measure(crank);
            var renderer = crank.GetComponent<Renderer>();
            var middle = renderer != null ? renderer.bounds.center : crank.transform.position;
            var center = r.start + r.axis * Vector3.Dot(middle - r.start, r.axis);
            var radius = Mathf.Max(0.5f, shape.radius * 0.85f);
            var u = Vector3.Cross(r.axis, Mathf.Abs(Vector3.Dot(r.axis, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var v = Vector3.Cross(r.axis, u);
            if (!Overlay.ToScreen(center, out var c) || !Overlay.ToScreen(center + u * radius, out var pu) || !Overlay.ToScreen(center + v * radius, out var pv)) return;
            // Screen angles turn the same way as the world's when the ring is seen from its +axis side.
            var sense = (pu.x - c.x) * (pv.y - c.y) - (pu.y - c.y) * (pv.x - c.x) >= 0f ? 1f : -1f;
            var mouse = Overlay.Mouse;
            var lit = dragging != null;
            const int Segments = 48;
            var near = float.MaxValue;
            var points = new Vector2[Segments + 1];
            var ok = true;
            for (var i = 0; i <= Segments; i++) {
                var a = i * Mathf.PI * 2f / Segments;
                if (!Overlay.ToScreen(center + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius, out points[i])) ok = false;
            }
            if (!ok) return;
            for (var i = 0; i < Segments; i++) near = Mathf.Min(near, Overlay.DistanceToSegment(mouse, points[i], points[i + 1]));
            hovered = dragging == null && near < 9f;
            lit |= hovered;
            var color = lit ? Theme.AccentHover : Theme.Accent;
            for (var i = 0; i < Segments; i++) Overlay.Line(points[i], points[i + 1], color, lit ? 3.5f : 2.5f);
            // Arrowheads round the ring: it turns either way.
            foreach (var k in new[] { 6, 30 }) Overlay.Arrowhead(points[k + 2], points[k], color, 12f, 10f);
            foreach (var k in new[] { 18, 42 }) Overlay.Arrowhead(points[k - 2], points[k], color, 12f, 10f);

            var screenAngle = Mathf.Atan2(mouse.y - c.y, mouse.x - c.x) * Mathf.Rad2Deg;
            if (dragging != null) {
                var delta = Mathf.DeltaAngle(lastScreenAngle, screenAngle) * sense;
                lastScreenAngle = screenAngle;
                // The crank's own turn; the run's first rotor is its shaft.
                var applied = run != null ? MotorRunner.Apply(run, delta) : 0f;
                turned += applied;
                if (Mathf.Abs(applied) < Mathf.Abs(delta) * 0.5f && Mathf.Abs(delta) > 0.5f)
                    Overlay.MouseText(run != null && run.system.blocked != 0 && Mathf.Sign(delta) == run.system.blocked ? "The ratchet holds it this way" : "It's at the end of its travel", Theme.Warning, null, true);
                else
                    Overlay.MouseText((turned / 360f).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " turns", Theme.Accent, null, true);
                if (!MouseInput.LeftButton.isPressed) Stop();
                return;
            }
            if (hovered) {
                Overlay.MouseText("Drag round to turn the crank", Color.white);
                if (MouseInput.LeftButton.wasPressedThisFrame) {
                    var system = GearTrain.SystemOf(r, GearTrain.Current());
                    run = MotorRunner.Begin(system, null, out var why);
                    if (run == null) {
                        Interface.Interface.Say("The crank can't turn: " + why, false);
                        Audio.Sounds.Play(Audio.Sounds.Error);
                        return;
                    }
                    dragging = crank;
                    rotor = r;
                    turned = 0f;
                    lastScreenAngle = screenAngle;
                }
            }
        }

        private void Stop() {
            if (run != null) MotorRunner.End(run);
            run = null;
            dragging = null;
            rotor = null;
        }
    }
}
