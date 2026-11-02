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
                if (lastHit.collider.gameObject.transform.root.gameObject.layer != Placement.PLACEMENT_LAYER)
                    return lastHit;
                return new RaycastHit();
            }

            // V2: holes are small and nearly edge-on from most angles; when only holes can be picked, a hole
            // near the mouse counts too.
            if (layerMask == LayerMask.GetMask("HoleCollisions"))
                return NearestHole(mouseRay);
            return new RaycastHit();
        }

        private RaycastHit NearestHole(Ray mouseRay) {
            Vector2 mouse = MouseInput.Position;
            var best = new RaycastHit();
            var bestPixels = HolePickPixels * Mathf.Max(1f, Screen.dpi / 96f);
            foreach (var candidate in Physics.SphereCastAll(mouseRay, 0.5f, Mathf.Infinity, layerMask)) {
                var hole = candidate.collider.transform;
                if (hole.root.gameObject.layer == Placement.PLACEMENT_LAYER)
                    continue;
                var screen = camera.WorldToScreenPoint(hole.position);
                if (screen.z <= 0f)
                    continue;
                var pixels = Vector2.Distance(mouse, screen);
                if (pixels >= bestPixels)
                    continue;
                bestPixels = pixels;
                best = candidate;
                // The hole's face that looks at the camera, as a direct hit on it would give.
                var axis = hole.forward;
                best.normal = Vector3.Dot(axis, mouseRay.direction) > 0f ? -axis : axis;
                best.point = hole.position;
            }
            return best;
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
