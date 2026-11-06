using System.Collections.Generic;
using UnityEngine;

namespace Protobot.Interface {
    // V2: what the mouse points at, like picking entities in a CAD program: a hole's centre (or the centre of one
    // of its faces), a corner of the part, the middle of an edge, a point along an edge, or else the point on the
    // face under the mouse. Holes win when close, then corners, midpoints, edges. Edges are the mesh's sharp
    // edges (where faces meet at an angle, and open borders), found once per mesh; corners are where those edges
    // turn. Close to the part's outline the mouse still snaps to its edges even if it's just off the part.
    public static class FeaturePicker {
        public enum Kind { None, Hole, HoleFace, Corner, Midpoint, Edge, Face }

        public struct Feature {
            public Kind kind;
            public Vector3 point;
            public Vector3 normal;     // the face's normal, or the hole's axis
            public Vector3 direction;  // along the edge (Edge, Midpoint)
            public Vector3 edgeA, edgeB;
            public GameObject part;
            public HoleCollider hole;
            public bool Valid => kind != Kind.None;
            public bool HasDirection => kind == Kind.Edge || kind == Kind.Midpoint;

            public string Describe() => kind switch {
                Kind.Hole => "Hole center",
                Kind.HoleFace => "Hole edge center",
                Kind.Corner => "Corner",
                Kind.Midpoint => "Edge midpoint",
                Kind.Edge => "Point on edge",
                Kind.Face => "Point on face",
                _ => "",
            };
        }

        private const float HolePixels = 16f, CornerPixels = 10f, MidpointPixels = 9f, EdgePixels = 8f;

        // Sharp edges and corners of a mesh, in its own space.
        private class Edges {
            public Vector3[] a, b;
            public Vector3[] corners;
        }
        private static readonly Dictionary<Mesh, Edges> edgeCache = new();

        private class Cache {
            public Matrix4x4 matrix;
            public Mesh mesh;
            public Vector3[] world;
        }
        private static readonly Dictionary<GameObject, Cache> caches = new();

        /// <summary>The part under the mouse (a hole's part counts), or null.</summary>
        public static GameObject PartUnderMouse(out RaycastHit hit) => PartAt(MouseInput.Position, out hit);

        private static GameObject PartAt(Vector2 screen, out RaycastHit hit) {
            var camera = Overlay.ViewCamera;
            var ray = camera.ScreenPointToRay(screen);
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

        /// <summary>The best feature near the mouse (on the part under it, or a part whose edge is just there).</summary>
        public static Feature Pick(GameObject only = null) {
            Vector2 mouseScreen = MouseInput.Position;
            var part = PartAt(mouseScreen, out var hit);
            var onFace = part != null && (only == null || part == only);
            if (!onFace) {
                part = null;
                // Just off the outline: look around the mouse for the part, edges only.
                var scale = Interface.Root != null ? Interface.Root.lossyScale.x : 1f;
                for (var i = 0; i < 8 && part == null; i++) {
                    var angle = i * Mathf.PI / 4f;
                    var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (EdgePixels - 1f) * scale;
                    var found = PartAt(mouseScreen + offset, out hit);
                    if (found != null && (only == null || found == only))
                        part = found;
                }
                if (part == null)
                    return default;
            }
            var mouse = Overlay.Mouse;
            var best = default(Feature);
            if (onFace) {
                best = new Feature { kind = Kind.Face, point = hit.point, normal = hit.normal, part = part };
                if (hit.collider.TryGetComponent(out HoleCollider hitHole))
                    best.normal = hitHole.transform.forward;
            }

            // Holes: the centre of the rim facing the camera (what a CAD program picks on a circular edge), the
            // hole's middle, and its far rim. The near rim wins when they're about as close (looking into the hole).
            var bestPixels = HolePixels;
            var view = Overlay.ViewCamera.transform.position;
            foreach (var hole in part.GetComponentsInChildren<HoleCollider>()) {
                var t = hole.transform;
                var half = t.forward * (Mathf.Abs(t.lossyScale.z) / 2f);
                var frontSign = Vector3.Dot(view - t.position, t.forward) >= 0f ? 1f : -1f;
                var near = t.position + half * frontSign;
                var far = t.position - half * frontSign;
                var biased = bestPixels;
                if (TryPoint(near, Kind.HoleFace, hole, t.forward * frontSign, ref best, ref biased, mouse, part, -4f))
                    bestPixels = biased;
                TryPoint(t.position, Kind.Hole, hole, t.forward, ref best, ref bestPixels, mouse, part);
                TryPoint(far, Kind.HoleFace, hole, -t.forward * frontSign, ref best, ref bestPixels, mouse, part);
            }
            if (best.kind == Kind.Hole || best.kind == Kind.HoleFace)
                return best;

            var edges = EdgesOf(part);
            if (edges == null)
                return best;
            var matrix = part.transform.localToWorldMatrix;
            // Only what's near the point under the mouse (a radius of about 30 pixels there).
            var local = part.transform.InverseTransformPoint(hit.point);
            var reach = WorldPerPixel(hit.point) * 30f / Mathf.Max(part.transform.lossyScale.x, 1e-4f);
            var normal = onFace ? best.normal : -Overlay.ViewCamera.transform.forward;

            bestPixels = CornerPixels;
            var cornerFound = false;
            foreach (var c in edges.corners) {
                if ((c - local).sqrMagnitude > reach * reach) continue;
                if (TryPoint(matrix.MultiplyPoint3x4(c), Kind.Corner, null, normal, ref best, ref bestPixels, mouse, part))
                    cornerFound = true;
            }
            if (cornerFound)
                return best;

            // Midpoints, then any point along an edge.
            var bestMid = MidpointPixels;
            var bestEdge = EdgePixels;
            var edgeIndex = -1;
            var midIndex = -1;
            var edgePoint = Vector3.zero;
            for (var i = 0; i < edges.a.Length; i++) {
                var a = edges.a[i];
                var b = edges.b[i];
                if (DistanceToSegment(local, a, b) > reach) continue;
                var wa = matrix.MultiplyPoint3x4(a);
                var wb = matrix.MultiplyPoint3x4(b);
                if (!Overlay.ToScreen(wa, out var sa) | !Overlay.ToScreen(wb, out var sb)) continue;
                var mid = (sa + sb) / 2f;
                var midPixels = Vector2.Distance(mid, mouse);
                if (midPixels < bestMid && Vector2.Distance(sa, sb) > 24f) {
                    bestMid = midPixels;
                    midIndex = i;
                }
                var ab = sb - sa;
                var t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(mouse - sa, ab) / ab.sqrMagnitude) : 0f;
                var pixels = Vector2.Distance(mouse, sa + ab * t);
                if (pixels < bestEdge) {
                    bestEdge = pixels;
                    edgeIndex = i;
                    edgePoint = ClosestOnSegmentToRay(wa, wb, Overlay.ViewCamera.ScreenPointToRay(mouseScreen));
                }
            }
            if (midIndex >= 0) {
                var wa = matrix.MultiplyPoint3x4(edges.a[midIndex]);
                var wb = matrix.MultiplyPoint3x4(edges.b[midIndex]);
                return new Feature {
                    kind = Kind.Midpoint, point = (wa + wb) / 2f, normal = normal, direction = (wb - wa).normalized,
                    edgeA = wa, edgeB = wb, part = part,
                };
            }
            if (edgeIndex >= 0) {
                var wa = matrix.MultiplyPoint3x4(edges.a[edgeIndex]);
                var wb = matrix.MultiplyPoint3x4(edges.b[edgeIndex]);
                return new Feature {
                    kind = Kind.Edge, point = edgePoint, normal = normal, direction = (wb - wa).normalized,
                    edgeA = wa, edgeB = wb, part = part,
                };
            }
            return best;
        }

        private static bool TryPoint(Vector3 world, Kind kind, HoleCollider hole, Vector3 normal, ref Feature best,
                                     ref float bestPixels, Vector2 mouse, GameObject part, float bias = 0f) {
            if (!Overlay.ToScreen(world, out var screen))
                return false;
            var pixels = Mathf.Max(0f, Vector2.Distance(screen, mouse) + bias);
            if (pixels >= bestPixels)
                return false;
            bestPixels = pixels;
            best = new Feature { kind = kind, point = world, normal = normal, part = part, hole = hole };
            return true;
        }

        private static float WorldPerPixel(Vector3 at) {
            var camera = Overlay.ViewCamera;
            var scale = Interface.Root != null ? Interface.Root.lossyScale.y : 1f;
            var perScreenPixel = camera.orthographic
                ? 2f * camera.orthographicSize / Screen.height
                : 2f * Vector3.Distance(camera.transform.position, at) * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height;
            return perScreenPixel * scale;
        }

        private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b) {
            var ab = b - a;
            var t = ab.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector3.Distance(p, a + ab * t);
        }

        // The point of segment ab closest to a ray (where the mouse is along an edge).
        private static Vector3 ClosestOnSegmentToRay(Vector3 a, Vector3 b, Ray ray) {
            var d1 = b - a;
            var d2 = ray.direction;
            var r = a - ray.origin;
            float aa = Vector3.Dot(d1, d1), bb = Vector3.Dot(d1, d2), cc = Vector3.Dot(d2, d2);
            float dd = Vector3.Dot(d1, r), ee = Vector3.Dot(d2, r);
            var denominator = aa * cc - bb * bb;
            var t = Mathf.Abs(denominator) < 1e-9f ? 0f : Mathf.Clamp01((bb * ee - cc * dd) / denominator);
            return a + d1 * t;
        }

        // ------------------------------------------------------------------ the mesh's edges and corners

        /// <summary>V2: the part's sharp edges in world space (for Convert Entities in the polycarbonate sketch).</summary>
        public static List<(Vector3 a, Vector3 b)> SharpEdges(GameObject part) {
            var list = new List<(Vector3, Vector3)>();
            var edges = part != null ? EdgesOf(part) : null;
            if (edges == null) return list;
            var matrix = part.transform.localToWorldMatrix;
            for (var i = 0; i < edges.a.Length; i++)
                list.Add((matrix.MultiplyPoint3x4(edges.a[i]), matrix.MultiplyPoint3x4(edges.b[i])));
            return list;
        }

        private static Edges EdgesOf(GameObject part) {
            var filter = part.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
                return null;
            var mesh = filter.sharedMesh;
            if (edgeCache.TryGetValue(mesh, out var edges))
                return edges;
            edges = FindEdges(mesh);
            if (edgeCache.Count > 96) {
                var dead = new List<Mesh>();
                foreach (var key in edgeCache.Keys) if (key == null) dead.Add(key);
                foreach (var key in dead) edgeCache.Remove(key);
                if (edgeCache.Count > 96) edgeCache.Clear();
            }
            edgeCache[mesh] = edges;
            return edges;
        }

        private static Edges FindEdges(Mesh mesh) {
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            // Weld vertices that sit at the same place (meshes split them along hard edges and UV seams).
            var ids = new Dictionary<Vector3Int, int>();
            var welded = new List<Vector3>();
            var map = new int[vertices.Length];
            for (var i = 0; i < vertices.Length; i++) {
                var key = Vector3Int.RoundToInt(vertices[i] * 4000f);
                if (!ids.TryGetValue(key, out var id)) {
                    id = welded.Count;
                    ids[key] = id;
                    welded.Add(vertices[i]);
                }
                map[i] = id;
            }
            // Each edge's triangle normals.
            var faces = new Dictionary<long, (Vector3 first, int count, bool sharp)>();
            for (var t = 0; t + 2 < triangles.Length; t += 3) {
                int i0 = map[triangles[t]], i1 = map[triangles[t + 1]], i2 = map[triangles[t + 2]];
                if (i0 == i1 || i1 == i2 || i0 == i2) continue;
                var n = Vector3.Cross(welded[i1] - welded[i0], welded[i2] - welded[i0]);
                if (n.sqrMagnitude < 1e-14f) continue;
                n.Normalize();
                AddEdge(faces, i0, i1, n);
                AddEdge(faces, i1, i2, n);
                AddEdge(faces, i2, i0, n);
            }
            var a = new List<Vector3>();
            var b = new List<Vector3>();
            var degree = new Dictionary<int, List<Vector3>>();
            foreach (var pair in faces) {
                var (_, count, sharp) = pair.Value;
                if (count == 2 && !sharp) continue;
                var i0 = (int)(pair.Key >> 32);
                var i1 = (int)(pair.Key & 0xffffffff);
                a.Add(welded[i0]);
                b.Add(welded[i1]);
                AddDirection(degree, i0, welded[i1] - welded[i0]);
                AddDirection(degree, i1, welded[i0] - welded[i1]);
            }
            // Corners: where sharp edges meet other than in a smooth run (a circle's rim isn't corners).
            var corners = new List<Vector3>();
            foreach (var pair in degree) {
                var directions = pair.Value;
                var corner = directions.Count != 2 || Vector3.Angle(directions[0], -directions[1]) > 30f;
                if (corner)
                    corners.Add(welded[pair.Key]);
            }
            return new Edges { a = a.ToArray(), b = b.ToArray(), corners = corners.ToArray() };
        }

        private static void AddEdge(Dictionary<long, (Vector3 first, int count, bool sharp)> faces, int i, int j, Vector3 normal) {
            var key = i < j ? ((long)i << 32) | (uint)j : ((long)j << 32) | (uint)i;
            if (faces.TryGetValue(key, out var entry))
                faces[key] = (entry.first, entry.count + 1, entry.sharp || Vector3.Angle(entry.first, normal) > 25f);
            else
                faces[key] = (normal, 1, false);
        }

        private static void AddDirection(Dictionary<int, List<Vector3>> degree, int vertex, Vector3 direction) {
            if (!degree.TryGetValue(vertex, out var list))
                degree[vertex] = list = new List<Vector3>(2);
            list.Add(direction.normalized);
        }

        /// <summary>The part's mesh vertices in world space (duplicates merged), cached until it moves.</summary>
        public static Vector3[] Corners(GameObject part) {
            var filter = part.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
                return System.Array.Empty<Vector3>();
            if (!caches.TryGetValue(part, out var cache) || cache.mesh != filter.sharedMesh) {
                var unique = new HashSet<Vector3Int>();
                var localPoints = new List<Vector3>();
                foreach (var v in filter.sharedMesh.vertices)
                    if (unique.Add(Vector3Int.RoundToInt(v * 2000f)))
                        localPoints.Add(v);
                cache = new Cache { mesh = filter.sharedMesh, world = new Vector3[localPoints.Count], matrix = Matrix4x4.zero };
                cache.world = localPoints.ToArray();
                caches[part] = cache;
                cacheLocal[part] = localPoints.ToArray();
                if (caches.Count > 64)
                    Prune();
            }
            var matrix = part.transform.localToWorldMatrix;
            if (matrix != cache.matrix) {
                cache.matrix = matrix;
                var localPoints = cacheLocal[part];
                for (var i = 0; i < localPoints.Length; i++)
                    cache.world[i] = matrix.MultiplyPoint3x4(localPoints[i]);
            }
            return cache.world;
        }
        private static readonly Dictionary<GameObject, Vector3[]> cacheLocal = new();

        private static void Prune() {
            var dead = new List<GameObject>();
            foreach (var pair in caches)
                if (pair.Key == null) dead.Add(pair.Key);
            foreach (var key in dead) {
                caches.Remove(key);
                cacheLocal.Remove(key);
            }
            if (caches.Count > 64) {
                caches.Clear();
                cacheLocal.Clear();
            }
        }

        /// <summary>Draws the picked feature: a ring for holes, a square for corners, the edge for edges.</summary>
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
                    Overlay.Square(p, 5.5f, Color.black);
                    Overlay.Square(p, 4.5f, color);
                    break;
                case Kind.Midpoint:
                    Overlay.Line(feature.edgeA, feature.edgeB, color, 2.5f);
                    Overlay.Triangle(p, 7f, Color.black);
                    Overlay.Triangle(p, 5.5f, color);
                    break;
                case Kind.Edge:
                    Overlay.Line(feature.edgeA, feature.edgeB, color, 2.5f);
                    Overlay.Disc(p, 4f, Color.black);
                    Overlay.Disc(p, 3f, color);
                    break;
                default:
                    Overlay.Disc(p, 3.5f, color);
                    break;
            }
        }

        /// <summary>The hover marker plus its name beside the mouse, as tools show what will be picked.</summary>
        public static void DrawHover(Feature feature, Color color) {
            Draw(feature, color);
            if (feature.Valid && Overlay.ToScreen(feature.point, out var p))
                Overlay.Text(p + new Vector2(0f, 22f), feature.Describe(), Theme.Text);
        }
    }
}
