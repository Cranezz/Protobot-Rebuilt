using System.Collections.Generic;
using UnityEngine;

namespace Protobot.Interface {
    // V2: what the mouse points at for measuring, like picking entities in a CAD program: a hole's centre (or
    // the centre of one of its faces), a corner of the part (a mesh vertex), or else the point on the face
    // under the mouse. Holes win when close, then corners.
    public static class FeaturePicker {
        public enum Kind { None, Hole, HoleFace, Corner, Face }

        public struct Feature {
            public Kind kind;
            public Vector3 point;
            public Vector3 normal;    // the face's normal, or the hole's axis
            public GameObject part;
            public HoleCollider hole;
            public bool Valid => kind != Kind.None;

            public string Describe() => kind switch {
                Kind.Hole => "Hole center",
                Kind.HoleFace => "Hole edge center",
                Kind.Corner => "Corner",
                Kind.Face => "Point on face",
                _ => "",
            };
        }

        private const float HolePixels = 16f, CornerPixels = 10f;

        private class Cache {
            public Matrix4x4 matrix;
            public Mesh mesh;
            public Vector3[] local;
            public Vector3[] world;
        }
        private static readonly Dictionary<GameObject, Cache> caches = new();

        /// <summary>The part under the mouse (a hole's part counts), or null.</summary>
        public static GameObject PartUnderMouse(out RaycastHit hit) {
            var camera = Overlay.ViewCamera;
            var ray = camera.ScreenPointToRay(MouseInput.Position);
            var mask = LayerMask.GetMask("Default", "HoleCollisions");
            if (Physics.Raycast(ray, out hit, Mathf.Infinity, mask))
                return PartOf(hit.collider.gameObject);
            return null;
        }

        public static GameObject PartOf(GameObject hitObject) {
            if (hitObject.GetComponent<HoleCollider>() != null && hitObject.transform.parent != null)
                return hitObject.transform.parent.gameObject;
            return hitObject.GetComponent<SavedObject>() != null ? hitObject : null;
        }

        /// <summary>The best feature near the mouse (on the part under it).</summary>
        public static Feature Pick() {
            var part = PartUnderMouse(out var hit);
            if (part == null)
                return default;
            var mouse = Overlay.Mouse;
            var best = new Feature { kind = Kind.Face, point = hit.point, normal = hit.normal, part = part };
            if (hit.collider.TryGetComponent(out HoleCollider hitHole))
                best = new Feature { kind = Kind.Face, point = hit.point, normal = hitHole.transform.forward, part = part };

            // Holes: their centres and face centres.
            var bestPixels = HolePixels;
            foreach (var hole in part.GetComponentsInChildren<HoleCollider>()) {
                var t = hole.transform;
                var half = t.forward * (Mathf.Abs(t.lossyScale.z) / 2f);
                TryPoint(t.position, Kind.Hole, hole, t.forward, ref best, ref bestPixels, mouse, part);
                TryPoint(t.position + half, Kind.HoleFace, hole, t.forward, ref best, ref bestPixels, mouse, part);
                TryPoint(t.position - half, Kind.HoleFace, hole, t.forward, ref best, ref bestPixels, mouse, part);
            }
            if (best.kind == Kind.Hole || best.kind == Kind.HoleFace)
                return best;

            // Corners.
            bestPixels = CornerPixels;
            foreach (var corner in Corners(part))
                TryPoint(corner, Kind.Corner, null, hit.normal, ref best, ref bestPixels, mouse, part);
            return best;
        }

        private static void TryPoint(Vector3 world, Kind kind, HoleCollider hole, Vector3 normal, ref Feature best,
                                     ref float bestPixels, Vector2 mouse, GameObject part) {
            if (!Overlay.ToScreen(world, out var screen))
                return;
            var pixels = Vector2.Distance(screen, mouse);
            if (pixels >= bestPixels)
                return;
            bestPixels = pixels;
            best = new Feature { kind = kind, point = world, normal = normal, part = part, hole = hole };
        }

        /// <summary>The part's mesh vertices in world space (duplicates merged), cached until it moves.</summary>
        public static Vector3[] Corners(GameObject part) {
            var filter = part.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
                return System.Array.Empty<Vector3>();
            if (!caches.TryGetValue(part, out var cache) || cache.mesh != filter.sharedMesh) {
                var unique = new HashSet<Vector3Int>();
                var local = new List<Vector3>();
                foreach (var v in filter.sharedMesh.vertices)
                    if (unique.Add(Vector3Int.RoundToInt(v * 2000f)))
                        local.Add(v);
                cache = new Cache { mesh = filter.sharedMesh, local = local.ToArray(), world = new Vector3[local.Count], matrix = Matrix4x4.zero };
                caches[part] = cache;
                if (caches.Count > 64)
                    Prune();
            }
            var matrix = part.transform.localToWorldMatrix;
            if (matrix != cache.matrix) {
                cache.matrix = matrix;
                for (var i = 0; i < cache.local.Length; i++)
                    cache.world[i] = matrix.MultiplyPoint3x4(cache.local[i]);
            }
            return cache.world;
        }

        private static void Prune() {
            var dead = new List<GameObject>();
            foreach (var pair in caches)
                if (pair.Key == null) dead.Add(pair.Key);
            foreach (var key in dead) caches.Remove(key);
            if (caches.Count > 64) caches.Clear();
        }

        /// <summary>Draws the picked feature: a ring for holes, a dot for corners and points.</summary>
        public static void Draw(Feature feature, Color color) {
            if (!feature.Valid || !Overlay.ToScreen(feature.point, out var p))
                return;
            switch (feature.kind) {
                case Kind.Hole:
                case Kind.HoleFace:
                    Overlay.Ring(p, 9f, color, 2f);
                    Overlay.Disc(p, 2.5f, color);
                    break;
                case Kind.Corner:
                    Overlay.Disc(p, 5f, Color.black);
                    Overlay.Disc(p, 4f, color);
                    break;
                default:
                    Overlay.Disc(p, 3.5f, color);
                    break;
            }
        }
    }
}
