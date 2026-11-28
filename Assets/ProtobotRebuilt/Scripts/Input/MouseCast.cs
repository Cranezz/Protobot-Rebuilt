using System.Collections.Generic;
using UnityEngine;

namespace Protobot {
[RequireComponent(typeof(Camera))]
    public class MouseCast : MonoBehaviour {
        [CacheComponent] private new Camera camera;
        public Ray ray => camera.ScreenPointToRay(MouseInput.Position);
        public RaycastHit hit {
            get {
                // V2: the original cast again every time this was read (several times a frame by every tool);
                // the hit is kept until the frame, the mouse or the layers change.
                Vector2 position = MouseInput.Position;
                if (cachedFrame == Time.frameCount && cachedPosition == position && cachedMask == layerMask)
                    return cachedHit;
                cachedFrame = Time.frameCount;
                cachedPosition = position;
                cachedMask = layerMask;
                cachedHit = Cast();
                return cachedHit;
            }
        }
        public bool overObj => (hit.collider != null);
        public new GameObject gameObject => overObj ? hit.collider.gameObject : null;
        public new Transform transform => gameObject.transform;

        public int layerMask;

        private int cachedFrame = -1;
        private Vector2 cachedPosition;
        private int cachedMask;
        private RaycastHit cachedHit;

        // V2: how close (in screen pixels at 96 dpi) the mouse has to be to a hole to pick it when the ray misses it.
        private const float HolePickPixels = 14f;

        private void Awake() {
            ResetLayerMask();
        }

        private RaycastHit Cast() {
            var mouseRay = ray;
            if (Physics.Raycast(mouseRay, out RaycastHit lastHit, Mathf.Infinity, layerMask)) {
                if (lastHit.collider.gameObject.transform.root.gameObject.layer != Placement.PLACEMENT_LAYER) {
                    // V2: while placing, a hole near the mouse wins over the part around it: ports sit a little inside
                    // a valve's or cylinder's body and small holes are hard to hit, so a fitting, screw or standoff
                    // over the body beside a hole goes into the hole.
                    // V2: not over a screw or shaft: the mouse is on the axle a wheel, gear or nut slides along.
                    if (Placement.IsPlacing && !lastHit.collider.CompareTag("HoleCollider") && (layerMask & (1 << HoleCollider.HOLE_COLLISIONS_LAYER)) != 0
                        && lastHit.collider.GetComponentInParent<Screw>() == null && lastHit.collider.GetComponentInParent<Shaft>() == null) {
                        var near = NearestHole(mouseRay, lastHit.distance + 0.35f);
                        if (near.collider != null) return near;
                    }
                    return lastHit;
                }
                return new RaycastHit();
            }

            // V2: holes are small and nearly edge-on from most angles; when only holes can be picked, a hole
            // near the mouse counts too.
            if (layerMask == LayerMask.GetMask("HoleCollisions") || (Placement.IsPlacing && (layerMask & (1 << HoleCollider.HOLE_COLLISIONS_LAYER)) != 0))
                return NearestHole(mouseRay, Mathf.Infinity);
            return new RaycastHit();
        }

        // maxDistance: how far from the camera the hole's near face may be (behind the part hit, it's hidden).
        private RaycastHit NearestHole(Ray mouseRay, float maxDistance) {
            Vector2 mouse = MouseInput.Position;
            var best = new RaycastHit();
            var bestPixels = HolePickPixels * Mathf.Max(1f, Screen.dpi / 96f);
            foreach (var candidate in Physics.SphereCastAll(mouseRay, 0.5f, Mathf.Infinity, layerMask)) {
                var hole = candidate.collider.transform;
                if (hole.root.gameObject.layer == Placement.PLACEMENT_LAYER)
                    continue;
                // V2: only holes (the cast's mask has the Default layer too: a gear's body near the mouse was taken for
                // a hole, and a wheel over a screw beside that gear fell back to the grid, unturned).
                if (!candidate.collider.CompareTag("HoleCollider"))
                    continue;
                var axis = hole.forward;
                var facing = Vector3.Dot(axis, mouseRay.direction) > 0f ? -axis : axis;
                var nearFace = hole.position + facing * hole.lossyScale.z / 2f;
                if (Vector3.Distance(mouseRay.origin, nearFace) > maxDistance)
                    continue;
                var screen = camera.WorldToScreenPoint(nearFace);   // V2: the face the part goes on, not the hole's middle
                if (screen.z <= 0f)
                    continue;
                var pixels = Vector2.Distance(mouse, screen);
                if (pixels >= bestPixels)
                    continue;
                bestPixels = pixels;
                best = candidate;
                // The hole's face that looks at the camera, as a direct hit on it would give.
                best.normal = facing;
                best.point = nearFace;
            }
            return best.collider != null ? TopOfStack(best, mouseRay, maxDistance) : best;
        }

        // V2 (build 88): holes in line (a bearing's hole over a C-channel's, a washer on a plate) are the same spot on
        // screen, and the lower one could be a pixel nearer the mouse: a screw went to the channel's face, half sunk in
        // the bearing (a tester's report). From the picked hole, up the stack of holes that touch it (each one's far
        // face on or past the near face of the one below), the top one wins; a hole further off (a C-channel's other
        // flange) isn't part of the stack.
        private static RaycastHit TopOfStack(RaycastHit best, Ray mouseRay, float maxDistance) {
            var axis = best.collider.transform.forward;
            var up = best.normal;   // toward the camera
            var found = new List<(RaycastHit hit, float near, float far)>();
            foreach (var candidate in Physics.SphereCastAll(new Ray(best.point + up * 2f, -up), 0.05f, 2.2f, 1 << HoleCollider.HOLE_COLLISIONS_LAYER, QueryTriggerInteraction.Collide)) {
                var hole = candidate.collider.transform;
                if (candidate.collider == best.collider || !candidate.collider.CompareTag("HoleCollider")) continue;
                if (hole.root.gameObject.layer == Placement.PLACEMENT_LAYER || hole.root.gameObject.layer == 12) continue;
                if (Vector3.Cross(hole.forward, axis).magnitude > 0.05f) continue;
                var off = hole.position - best.collider.transform.position;
                if ((off - axis * Vector3.Dot(off, axis)).magnitude > 0.04f) continue;   // on the same line
                var middle = Vector3.Dot(hole.position - best.point, up);
                var half = hole.lossyScale.z / 2f;
                found.Add((candidate, middle + half, middle - half));
            }
            found.Sort((a, b) => a.far.CompareTo(b.far));
            var top = best;
            var topNear = 0f;   // the picked hole's near face is best.point
            foreach (var f in found) {
                if (f.near <= topNear + 0.005f) continue;          // not above the stack's top
                if (f.far > topNear + 0.03f) break;                // a gap: not touching
                var nearFace = best.point + up * f.near;
                if (Vector3.Distance(mouseRay.origin, nearFace) > maxDistance + 0.5f) break;
                top = f.hit;
                top.normal = up;
                top.point = nearFace;
                topNear = f.near;
            }
            return top;
        }

        public void ResetLayerMask() => layerMask = LayerMask.GetMask("Default", "HoleCollisions");

        public void SetHolesOnlyMode(bool value) {
            if (value)
                layerMask = LayerMask.GetMask("HoleCollisions");
            else
                ResetLayerMask();
        }
    }
}
