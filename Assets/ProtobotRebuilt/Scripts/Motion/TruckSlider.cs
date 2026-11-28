using System.Collections.Generic;
using System.Linq;
using Protobot.Interface;
using Protobot.StateSystems;
using Protobot.Tools;
using UnityEngine;

namespace Protobot.Motion {
    // V2 (build 88): sliding a truck along its slide member or rail in the builder, like a cylinder's rod (RodSlider): a
    // selected truck (Transform tool) shows an arrow both ways along its member, out from its face; drag it to slide the
    // truck and everything fastened to it (a stage, and what rides on that stage: a cascade's carriage) along the member
    // (the Transform tool's snap, Shift free). It can be slid off either end: once it's wholly off the member it's no
    // longer held by it (SlideTruck.Check), and the parts move on their own. One undo step a drag.
    public class TruckSlider : MonoBehaviour {
        private static TruckSlider instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Make() {
            if (instance != null) return;
            instance = new GameObject("Truck Slider").AddComponent<TruckSlider>();
        }

        /// <summary>The mouse is on a truck's arrow (or dragging it).</summary>
        public static bool Busy => instance != null && (instance.dragging != null || instance.hovered);

        private SlideTruck dragging;
        private GameObject member;
        private Vector3 axis, lineStart;
        private float startX, low, high, memberMin, memberMax;
        private List<GameObject> riders;
        private List<Vector3> riderStart;
        private List<IElement> before;
        private Vector2 pressAt;
        private bool hovered;
        private string refused;

        // How far past the member's ends a truck can be slid (beyond wholly off it).
        private const float OffBeyond = 1.5f, Half = 1f;

        private void Update() {
            hovered = false;
            var truck = dragging;
            if (truck == null) {
                var part = ToolRail.Current == "Transform" && !Placement.IsPlacing && !Sandbox.Running ? SelectTool.SelectedPart : null;
                truck = part != null ? part.GetComponent<SlideTruck>() : null;
                if (truck != null && truck.Member == null) truck = null;
            }
            if (truck == null || truck.gameObject.IsDeleted() || Sandbox.Running) {
                Stop(false);
                return;
            }
            var camera = Overlay.ViewCamera;
            if (camera == null) return;
            var m = dragging != null ? member : truck.Member;
            if (m == null) return;
            var along = m.transform.right;
            if (dragging == null && Mathf.Abs(Vector3.Dot(along, camera.transform.forward)) > 0.97f) return;   // (pointing at the viewer)
            var center = truck.TryGetComponent(out Renderer r) ? r.bounds.center : truck.transform.position;
            var perPixel = MirrorTool.PerPixel(camera, center);
            // The arrow's middle: out from the truck toward the viewer's side, clear of the part.
            var outward = Vector3.ProjectOnPlane(camera.transform.position - center, along).normalized;
            var mid = center + outward * (34f * perPixel);
            var a = mid - along * (40f * perPixel);
            var b = mid + along * (40f * perPixel);
            Overlay.Begin();
            if (!Overlay.ToScreen(a, out var aAt) || !Overlay.ToScreen(b, out var bAt) || !Overlay.ToScreen(mid, out var midAt)) return;
            var mouse = Overlay.Mouse;
            hovered = dragging == null && Overlay.DistanceToSegment(mouse, aAt, bAt) < 9f;
            var lit = hovered || dragging != null;
            var color = lit ? Theme.AccentHover : Theme.Accent;

            // The member's length (where the truck can sit), drawn while it's lit.
            var (point, _, tLow, tHigh) = SlideTruck.Track(m);
            if (lit) {
                var x0 = Vector3.Dot(center - point, along);
                var p0 = mid + along * (tLow - x0);
                var p1 = mid + along * (tHigh - x0);
                if (Overlay.ToScreen(p0, out var p0At) && Overlay.ToScreen(p1, out var p1At)) {
                    var track = new Color(1f, 1f, 1f, 0.35f);
                    var dir = (p1At - p0At).sqrMagnitude > 1f ? (p1At - p0At).normalized : Vector2.right;
                    var side = new Vector2(-dir.y, dir.x);
                    Overlay.Line(p0At, p1At, track, 2f);
                    Overlay.Line(p0At - side * 7f, p0At + side * 7f, track, 2f);
                    Overlay.Line(p1At - side * 7f, p1At + side * 7f, track, 2f);
                }
            }
            // A gizmo-style arrow both ways along the member.
            Overlay.Line(aAt, bAt, color, lit ? 3.5f : 3f);
            var sd = (bAt - aAt).sqrMagnitude > 1f ? (bAt - aAt).normalized : Vector2.right;
            Overlay.Arrowhead(bAt + sd * 14f, bAt, color, 14f, 12f);
            Overlay.Arrowhead(aAt - sd * 14f, aAt, color, 14f, 12f);
            Overlay.Disc(midAt, lit ? 5.5f : 4.5f, color);

            if (dragging != null) {
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                var moved = MirrorTool.AlongLine(camera, lineStart, axis);
                var x = startX + moved;
                var snap = RodSlider.Step;
                if (snap > 0f && (keyboard == null || !keyboard.shiftKey.isPressed)) x = startX + Mathf.Round(moved / snap) * snap;
                x = Mathf.Clamp(x, memberMin - Half - OffBeyond, memberMax + Half + OffBeyond);
                if (Vector2.Distance(mouse, pressAt) > 1f) {
                    var delta = axis * (x - startX);
                    for (var i = 0; i < riders.Count; i++) if (riders[i] != null) riders[i].transform.position = riderStart[i] + delta;
                }
                // Where it is: from the near end of the member, or off it.
                var off = x < memberMin - Half + 0.02f || x > memberMax + Half - 0.02f;
                var hanging = !off && (x < low - 0.001f || x > high + 0.001f);
                var text = off ? "Off the end: not held by it any more"
                    : hanging ? "Partly off the end (" + Inch.Format(Mathf.Min(x - (memberMin - Half), memberMax + Half - x)) + " still on)"
                    : Inch.Format(Mathf.Min(x - low, high - x)) + " from the end";
                Overlay.MouseText(text, off ? Theme.Warning : Theme.Accent, null, true);
                if (!MouseInput.LeftButton.isPressed) Stop(true);
                return;
            }
            if (hovered) {
                Overlay.MouseText(refused ?? "Drag to slide it along" + (RodSlider.Step > 0f ? " (snaps " + Inch.Format(RodSlider.Step) + ", Shift: free)" : ""), refused != null ? Theme.Warning : Color.white);
                if (MouseInput.LeftButton.wasPressedThisFrame) Begin(truck, mouse);
            }
            else refused = null;
        }

        private void Begin(SlideTruck truck, Vector2 mouse) {
            var m = truck.Member;
            var moving = Moving(truck, m, out var problem);
            if (moving == null) {
                refused = problem;
                Audio.Sounds.Play(Audio.Sounds.Error);
                return;
            }
            dragging = truck;
            member = m;
            axis = m.transform.right;
            var (point, _, tLow, tHigh) = SlideTruck.Track(m);
            low = tLow;
            high = tHigh;
            memberMin = tLow - Half;
            memberMax = tHigh + Half;
            startX = Vector3.Dot(truck.transform.position - point, axis);
            lineStart = truck.transform.position;
            riders = moving;
            riderStart = moving.Select(o => o.transform.position).ToList();
            before = moving.Select(o => (IElement)new ObjectElement(o)).ToList();
            pressAt = mouse;
        }

        /// <summary>What slides with a truck: everything fastened to it that isn't the body its member is part of.
        /// The member's body is its group with every truck's hold on a member along the same way let go (trucks slide
        /// there); from the truck, everything reached without going into that body moves (a stage, and a carriage riding
        /// in that stage). Null when something moving is also fastened to the member's body directly.</summary>
        public static List<GameObject> Moving(SlideTruck truck, GameObject member, out string problem) {
            problem = null;
            var axis = member.transform.right;
            var sliding = new HashSet<(GameObject, GameObject)>();
            foreach (var t in FindObjectsByType<SlideTruck>(FindObjectsSortMode.None))
                if (t != null && t.Member != null && Mathf.Abs(Vector3.Dot(t.Member.transform.right, axis)) > 0.999f) {
                    sliding.Add((t.gameObject, t.Member));
                    sliding.Add((t.Member, t.gameObject));
                }
            HashSet<GameObject> Reach(GameObject from, HashSet<GameObject> avoid, bool cutSliding) {
                var seen = new HashSet<GameObject> { from };
                var queue = new Queue<GameObject>();
                queue.Enqueue(from);
                while (queue.Count > 0) {
                    var o = queue.Dequeue();
                    foreach (var n in GroupManager.Neighbours(o)) {
                        if (n == null || n.IsDeleted() || seen.Contains(n) || (avoid != null && avoid.Contains(n))) continue;
                        if (cutSliding && sliding.Contains((o, n))) continue;
                        seen.Add(n);
                        queue.Enqueue(n);
                    }
                }
                return seen;
            }
            var fixedBody = Reach(member, null, true);
            if (fixedBody.Contains(truck.gameObject)) {
                problem = "It's fastened to what it slides in: it can't slide";
                return null;
            }
            var moving = Reach(truck.gameObject, fixedBody, false);
            // Something moving held to the member's body by more than a sliding truck: it couldn't slide.
            foreach (var o in moving)
                foreach (var n in GroupManager.Neighbours(o))
                    if (n != null && fixedBody.Contains(n) && !sliding.Contains((o, n))) {
                        problem = "It's fastened to what it slides in (through " + PartLabels.Short(o) + "): it can't slide";
                        return null;
                    }
            return moving.Where(o => o != null && o.GetComponent<SavedObject>() != null).ToList();
        }

        private void Stop(bool commit) {
            if (dragging == null) return;
            var changed = riders != null && riders.Count > 0 && riders[0] != null && (riders[0].transform.position - riderStart[0]).sqrMagnitude > 1e-8f;
            if (commit && changed) {
                StateSystem.AddElements(before);
                StateSystem.AddState(new State(riders.Where(o => o != null).Select(o => (IElement)new ObjectElement(o)).ToList()));
            }
            else if (!commit && changed)
                for (var i = 0; i < riders.Count; i++) if (riders[i] != null) riders[i].transform.position = riderStart[i];
            dragging.Check();
            dragging = null;
            member = null;
            riders = null;
            before = null;
        }
    }
}
