using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Protobot.EditorTools {
    // V2: takes out the parts of a model nobody can see: pieces fully inside it (a motor's gears and circuit board,
    // the inner faces of a hollow shell). A piece is a set of triangles joined by shared vertices (a CAD face or a
    // body); it goes only when none of its triangles can be seen from outside, looking from 64 directions round it
    // (a ray from far away toward each triangle's middle that reaches that triangle). So a visible surface is never
    // cut into: what's left looks exactly the same and keeps its exact size. The saw would show a cut motor hollow.
    // Run on the baked VEX meshes (VEX Parts/Meshes, rewritten in place: they're generated) and, as copies, on the
    // original's motor models. Tools > Protobot > Remove Hidden Insides.
    public static class HiddenGeometry {
        private const int Directions = 64;
        private static Vector3[] directions;

        private static Vector3[] Dirs() {
            if (directions != null) return directions;
            directions = new Vector3[Directions];
            var golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (var i = 0; i < Directions; i++) {
                var y = 1f - 2f * (i + 0.5f) / Directions;
                var r = Mathf.Sqrt(1f - y * y);
                directions[i] = new Vector3(Mathf.Cos(golden * i) * r, y, Mathf.Sin(golden * i) * r);
            }
            return directions;
        }

        /// <summary>Removes the hidden pieces from a readable mesh in place; returns (triangles before, after).</summary>
        public static (int before, int after) Strip(Mesh mesh) {
            var vertices = mesh.vertices;
            var subs = Enumerable.Range(0, mesh.subMeshCount).Select(mesh.GetTriangles).ToArray();
            var all = subs.SelectMany(t => t).ToArray();
            var count = all.Length / 3;
            if (count == 0) return (0, 0);

            // Pieces: triangles joined through shared vertex indices (union-find).
            var parent = Enumerable.Range(0, vertices.Length).ToArray();
            int Find(int x) {
                while (parent[x] != x) x = parent[x] = parent[parent[x]];
                return x;
            }
            for (var t = 0; t < count; t++) {
                int a = Find(all[3 * t]), b = Find(all[3 * t + 1]), c = Find(all[3 * t + 2]);
                parent[b] = a;
                parent[Find(c)] = Find(a);
            }
            var pieceOf = new int[count];
            var pieces = new Dictionary<int, List<int>>();
            for (var t = 0; t < count; t++) {
                var p = Find(all[3 * t]);
                pieceOf[t] = p;
                if (!pieces.TryGetValue(p, out var list)) pieces[p] = list = new List<int>();
                list.Add(t);
            }

            // Look at each piece from outside until one of its triangles is seen.
            var probe = new GameObject("visibility probe") { hideFlags = HideFlags.HideAndDontSave };
            probe.transform.position = new Vector3(100000f, 100000f, 100000f);
            var collider = probe.AddComponent<MeshCollider>();
            collider.cookingOptions = MeshColliderCookingOptions.None;   // keeps triangle numbers as they are
            collider.sharedMesh = mesh;
            Physics.SyncTransforms();
            var offset = probe.transform.position;
            var reach = mesh.bounds.extents.magnitude * 2f + 1f;
            var seen = new HashSet<int>();
            foreach (var (piece, triangles) in pieces) {
                // The biggest triangles first: they're the likeliest to be seen.
                foreach (var t in triangles.OrderByDescending(t => Area(vertices, all, t))) {
                    if (Seen(collider, vertices, all, t, offset, reach)) {
                        seen.Add(piece);
                        break;
                    }
                }
            }
            Object.DestroyImmediate(probe);
            if (seen.Count == pieces.Count) return (count, count);

            // Rebuild with only the seen pieces' triangles, and only the vertices they use.
            var used = new Dictionary<int, int>();
            var keep = new List<int>[subs.Length];
            var start = 0;
            for (var s = 0; s < subs.Length; s++) {
                keep[s] = new List<int>();
                for (var i = 0; i < subs[s].Length; i += 3) {
                    var t = (start + i) / 3;
                    if (!seen.Contains(pieceOf[t])) continue;
                    for (var k = 0; k < 3; k++) {
                        var v = subs[s][i + k];
                        if (!used.TryGetValue(v, out var n)) used[v] = n = used.Count;
                        keep[s].Add(n);
                    }
                }
                start += subs[s].Length;
            }
            var order = used.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToArray();
            var normals = mesh.normals;
            var tangents = mesh.tangents;
            var colors = mesh.colors;
            var uvs = new List<Vector4>[8];
            for (var c = 0; c < 8; c++) {
                if (!mesh.HasVertexAttribute((VertexAttribute)((int)VertexAttribute.TexCoord0 + c))) continue;
                uvs[c] = new List<Vector4>();
                mesh.GetUVs(c, uvs[c]);
            }
            var bounds = mesh.bounds;
            mesh.Clear();
            mesh.indexFormat = order.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = order.Select(i => vertices[i]).ToArray();
            if (normals.Length == vertices.Length) mesh.normals = order.Select(i => normals[i]).ToArray();
            if (tangents.Length == vertices.Length) mesh.tangents = order.Select(i => tangents[i]).ToArray();
            if (colors.Length == vertices.Length) mesh.colors = order.Select(i => colors[i]).ToArray();
            for (var c = 0; c < 8; c++)
                if (uvs[c] != null) mesh.SetUVs(c, order.Select(i => uvs[c][i]).ToList());
            mesh.subMeshCount = subs.Length;
            for (var s = 0; s < subs.Length; s++) mesh.SetTriangles(keep[s], s, false);
            mesh.bounds = bounds;   // the same box as before (placing and measuring use it)
            return (count, keep.Sum(k => k.Count) / 3);
        }

        private static float Area(Vector3[] v, int[] all, int t) =>
            Vector3.Cross(v[all[3 * t + 1]] - v[all[3 * t]], v[all[3 * t + 2]] - v[all[3 * t]]).sqrMagnitude;

        private static bool Seen(MeshCollider collider, Vector3[] v, int[] all, int t, Vector3 offset, float reach) {
            var middle = (v[all[3 * t]] + v[all[3 * t + 1]] + v[all[3 * t + 2]]) / 3f + offset;
            foreach (var d in Dirs()) {
                var ray = new Ray(middle + d * reach, -d);
                if (collider.Raycast(ray, out var hit, reach * 2f) && (hit.triangleIndex == t || (hit.point - middle).sqrMagnitude < 1e-8f))
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ what it runs on

        public const string VexMeshes = "Assets/ProtobotRebuilt/VEX Parts/Meshes";
        public const string Optimized = "Assets/ProtobotRebuilt/Models/Optimized";

        /// <summary>One baked VEX family's meshes, in place. Returns a line for the log.</summary>
        public static string StripFamily(string assetPath) {
            long before = 0, after = 0;
            foreach (var mesh in AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Mesh>()) {
                if (!mesh.isReadable) continue;
                var (b, a) = Strip(mesh);
                before += b;
                after += a;
                EditorUtility.SetDirty(mesh);
            }
            AssetDatabase.SaveAssets();
            return $"{Path.GetFileNameWithoutExtension(assetPath)}: {before / 1000}k -> {after / 1000}k triangles";
        }

        [MenuItem("Tools/Protobot/Remove Hidden Insides")]
        public static void All() {
            var lines = new List<string>();
            var files = Directory.GetFiles(VexMeshes, "*.asset");
            for (var i = 0; i < files.Length; i++) {
                EditorUtility.DisplayProgressBar("Remove hidden insides", Path.GetFileName(files[i]), (float)i / files.Length);
                lines.Add(StripFamily(files[i].Replace('\\', '/')));
            }
            EditorUtility.ClearProgressBar();
            Debug.Log("[Hidden insides]\n" + string.Join("\n", lines));
        }
    }
}
