using System;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Protobot {
    [Serializable]
    public class AluminumSubParts : MonoBehaviour {
        [SerializeField] private GameObject start;
        [SerializeField] private GameObject mid;
        [SerializeField] private GameObject mid5Start;
        [SerializeField] private GameObject mid5End;
        [SerializeField] private GameObject end;
        [SerializeField] private Material material;
        [SerializeField] private int maxHoleCount = 35;

        // V2: a part is cut from full-length stock (maxHoleCount holes), the way the saw cuts, instead of being
        // put together from segments: every length gets a clean, capped end (the original only had the end
        // segment on full-length parts). Cut meshes are kept per length.
        [NonSerialized] private Mesh stock;
        [NonSerialized] private readonly System.Collections.Generic.Dictionary<int, Mesh> cutMeshes = new();

        public Mesh GetMesh(int holeCount) {
            holeCount = Mathf.Clamp(holeCount, 1, maxHoleCount);
            if (cutMeshes.TryGetValue(holeCount, out var cached) && cached != null)
                return cached;
            var full = Stock();
            if (holeCount == maxHoleCount)
                return full;

            // Keep the first holeCount holes: cut in the middle of the gap after the last one, then centre them.
            var cutAt = GetXPos(holeCount - 1, maxHoleCount) + 0.25f;
            var mesh = Tools.PartCutting.Slice(full, new Vector3(cutAt, 0f, 0f), Vector3.left);
            if (mesh == null)
                return Segments(holeCount);
            var shift = 0.25f * (maxHoleCount - holeCount);
            var vertices = mesh.vertices;
            for (var i = 0; i < vertices.Length; i++)
                vertices[i].x += shift;
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            mesh.name = name + " (" + holeCount + ")";
            cutMeshes[holeCount] = mesh;
            return mesh;
        }

        private Mesh Stock() {
            if (stock == null) {
                stock = VexStock() ?? Seamless(Segments(maxHoleCount, true), maxHoleCount);
                stock.name = name + " (" + maxHoleCount + ")";
            }
            return stock;
        }

        // V2: the full-length model from the VEX-CAD library, as VEX makes it, is the stock every length is cut
        // from (the segments are only used for parts without one). Family prefab and part name per sub-part.
        private static readonly System.Collections.Generic.Dictionary<string, (string family, string part)> VexModels = new() {
            { "1x2 C-Channel", ("C-Channel Stock", "1x2x1 Aluminum C Channel (35)") },
            { "1x3 C-Channel", ("C-Channel Stock", "1x3x1 Aluminum C Channel (35)") },
            { "1x5 C-Channel", ("C-Channel Stock", "1x5x1 Aluminum C Channel (35)") },
            { "2x2 U-Channel", ("C-Channel Stock", "2x2x2 Aluminum C Channel (20)") },
            { "1x1 Angle", ("Angle Stock", "1x1 Aluminum Angle (35)") },
            { "2x2 Angle", ("Angle Stock", "2x2 Aluminum Angle (35)") },
            { "3x3 Angle", ("Angle Stock", "3x3 Steel Angle (35)") },
        };

        // The VEX model turned and moved onto this part's frame (along X, centred), found by lining its holes up
        // with the original's: the turn (of the 24 square ones) and shift where every hole lands on one.
        private Mesh VexStock() {
            if (!VexModels.TryGetValue(name, out var source)) return null;
            var family = Resources.Load<GameObject>("Part Prefabs/VEX/" + source.family);
            var model = family != null ? family.transform.Find(source.part) : null;
            if (model == null || !model.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
                return null;
            var vexMesh = filter.sharedMesh;

            // Holes as (centre, axis); a hole only matches one with the same axis.
            var ours = new System.Collections.Generic.List<(Vector3 p, Vector3 a)>();
            for (var i = 1; i <= maxHoleCount; i++)
                foreach (var hole in GetSubPart(i).GetComponentsInChildren<HoleCollider>(true)) {
                    var p = hole.transform.localPosition;
                    p.x += GetXPos(i - 1, maxHoleCount);
                    ours.Add((p, hole.transform.localRotation * Vector3.forward));
                }
            var theirs = new System.Collections.Generic.List<(Vector3 p, Vector3 a)>();
            foreach (var hole in model.GetComponentsInChildren<HoleCollider>(true))
                theirs.Add((model.InverseTransformPoint(hole.transform.position), model.InverseTransformDirection(hole.transform.forward)));
            if (ours.Count == 0 || theirs.Count == 0) return null;

            var ourBox = new Bounds(ours[0].p, Vector3.zero);
            foreach (var o in ours) ourBox.Encapsulate(o.p);
            Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            var best = float.MaxValue;
            var bestTurn = Quaternion.identity;
            var bestShift = Vector3.zero;
            foreach (var forward in axes)
                foreach (var up in axes) {
                    if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.1f) continue;
                    var turn = Quaternion.LookRotation(forward, up);
                    // Only turns that lay the model along X.
                    if (Mathf.Abs((turn * vexMesh.bounds.size).x) < vexMesh.bounds.size.magnitude * 0.8f) continue;
                    // Start with the two sets of holes' boxes centred on each other.
                    var theirBox = new Bounds(turn * theirs[0].p, Vector3.zero);
                    foreach (var t in theirs) theirBox.Encapsulate(turn * t.p);
                    var shift = ourBox.center - theirBox.center;
                    var error = 0f;
                    for (var pass = 0; pass < 4; pass++) {
                        var offset = Vector3.zero;
                        var matched = 0;
                        error = 0f;
                        foreach (var o in ours) {
                            var nearest = Vector3.zero;
                            var distance = float.MaxValue;
                            foreach (var t in theirs) {
                                if (Mathf.Abs(Vector3.Dot(turn * t.a, o.a)) < 0.9f) continue;  // same hole axis only
                                var d = (turn * t.p + shift - o.p).sqrMagnitude;
                                if (d < distance) { distance = d; nearest = turn * t.p + shift; }
                            }
                            if (distance == float.MaxValue) { error += 1f; continue; }
                            offset += o.p - nearest;
                            matched++;
                            error += Mathf.Sqrt(distance);
                        }
                        error /= ours.Count;
                        if (matched > 0) shift += offset / matched;
                    }
                    if (error < best) { best = error; bestTurn = turn; bestShift = shift; }
                }
            if (best > 0.05f) {
                Debug.LogWarning("V2: the VEX model " + source.part + " doesn't line up with " + name + "'s holes (" + best.ToString("F3") + "\"); using segments");
                return null;
            }

            var vertices = vexMesh.vertices;
            var normals = vexMesh.normals;
            for (var i = 0; i < vertices.Length; i++) vertices[i] = bestTurn * vertices[i] + bestShift;
            for (var i = 0; i < normals.Length; i++) normals[i] = bestTurn * normals[i];
            var triangles = new System.Collections.Generic.List<int>();
            for (var s = 0; s < vexMesh.subMeshCount; s++) triangles.AddRange(vexMesh.GetTriangles(s));
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices;
            if (normals.Length == vertices.Length) mesh.normals = normals;
            mesh.SetTriangles(triangles, 0);
            if (normals.Length != vertices.Length) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // The original's way: one segment per hole. V2: plain makes every middle segment the plain one (the
        // segments every 5th hole are shaped a little differently and showed as marks along a long channel).
        private Mesh Segments(int holeCount, bool plain = false) {
            CombineInstance[] combine = new CombineInstance[holeCount];

            for (int i = 0; i < holeCount; i++) {
                var subPart = plain && i > 0 && i < holeCount - 1 ? mid : GetSubPart(i + 1);
                var meshFilter = subPart.GetComponent<MeshFilter>();

                combine[i].mesh = meshFilter.sharedMesh;

                var tMatrix = meshFilter.transform.localToWorldMatrix;
                tMatrix[0, 3] = GetXPos(i, holeCount);
                tMatrix[1, 3] = 0; // resets Y pos
                tMatrix[2, 3] = 0; // resets Z pos

                combine[i].transform = tMatrix;
            }

            Mesh newMesh = new Mesh();
            newMesh.indexFormat = IndexFormat.UInt32;
            newMesh.CombineMeshes(combine);
            newMesh.RecalculateNormals();

            return newMesh;
        }

        // V2: the segments touch but aren't joined: each has its own end walls inside the stock and its own edge
        // vertices, which showed as seams along a long channel. Drop the walls between segments and join the
        // vertices that meet (same place, normals within 30°), then smooth across the joins.
        private static Mesh Seamless(Mesh mesh, int holeCount) {
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var triangles = mesh.triangles;
            const float Epsilon = 0.002f;
            float firstEnd = -0.25f * holeCount, lastEnd = 0.25f * holeCount;
            bool OnInnerJoin(float x) {
                if (x < firstEnd + Epsilon || x > lastEnd - Epsilon) return false;
                var steps = (x - firstEnd) / 0.5f;
                return Mathf.Abs(steps - Mathf.Round(steps)) * 0.5f < Epsilon;
            }

            var kept = new System.Collections.Generic.List<int>(triangles.Length);
            for (var i = 0; i < triangles.Length; i += 3) {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                var normal = Vector3.Cross(b - a, c - a);
                var wall = normal.sqrMagnitude > 1e-12f && Mathf.Abs(normal.normalized.x) > 0.99f
                           && OnInnerJoin(a.x) && Mathf.Abs(b.x - a.x) < Epsilon && Mathf.Abs(c.x - a.x) < Epsilon;
                if (wall) continue;
                kept.Add(triangles[i]);
                kept.Add(triangles[i + 1]);
                kept.Add(triangles[i + 2]);
            }

            // Join vertices: same place (to 1/1000") and a normal within 30°.
            var map = new int[vertices.Length];
            var buckets = new System.Collections.Generic.Dictionary<Vector3Int, System.Collections.Generic.List<int>>();
            var outVertices = new System.Collections.Generic.List<Vector3>();
            var outNormals = new System.Collections.Generic.List<Vector3>();
            for (var i = 0; i < vertices.Length; i++) {
                var key = Vector3Int.RoundToInt(vertices[i] * 1000f);
                var normal = normals.Length > i ? normals[i] : Vector3.zero;
                if (!buckets.TryGetValue(key, out var list))
                    buckets[key] = list = new System.Collections.Generic.List<int>();
                var found = -1;
                foreach (var candidate in list)
                    if (Vector3.Dot(outNormals[candidate], normal) > 0.866f) { found = candidate; break; }
                if (found < 0) {
                    found = outVertices.Count;
                    outVertices.Add(vertices[i]);
                    outNormals.Add(normal);
                    list.Add(found);
                }
                map[i] = found;
            }
            for (var i = 0; i < kept.Count; i++)
                kept[i] = map[kept[i]];

            var result = new Mesh { indexFormat = IndexFormat.UInt32 };
            result.SetVertices(outVertices);
            result.SetTriangles(kept, 0);
            result.RecalculateNormals();
            result.RecalculateBounds();
            return result;
        }

        public float GetXPos(int holeIndex, int holeCount) => 0.5f * ((-holeCount + 1) / 2f + holeIndex);

        public GameObject GeneratePart(int holeCount) {
            // make a new game object with a mesh from GetMesh(holeCount)
            // iterate thru all sub parts and add their holes with their LOCAL POSITIONS shifted by x values

            var newPart = new GameObject("Aluminum Part (" + holeCount + ")");
            Mesh mesh = GetMesh(holeCount);
            newPart.AddComponent<MeshFilter>().sharedMesh = mesh;
            newPart.AddComponent<MeshCollider>().sharedMesh = mesh;
            newPart.AddComponent<MeshRenderer>().material = material;
            
            GenerateHoles(newPart, holeCount);
            
            return newPart;
        }
        
        public void GenerateHoles(GameObject obj, int holeCount) {
            // iterate thru each subpart
            for (int i = 1; i <= holeCount; i++) {
                GameObject subPart = GetSubPart(i);

                foreach (var holeCollider in subPart.GetComponentsInChildren<HoleCollider>()) {
                    var pos = holeCollider.transform.localPosition;
                    pos.x += GetXPos(i - 1, holeCount); // correctly positions hole

                    var rot = holeCollider.transform.rotation;
                    
                    Instantiate(holeCollider.gameObject, pos, rot, obj.transform);
                }
            }
        }

        public GameObject GetSubPart(int curHole) {
            if (curHole == 1) 
                return start;
            
            if (curHole == maxHoleCount)
                return end;

            if (curHole % 5 == 0)
                return mid5Start;

            if ((curHole - 1) % 5 == 0)
                return mid5End;

            return mid;
        }
    }
}