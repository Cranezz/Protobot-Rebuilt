using System.Collections.Generic;
using System.Linq;
using Protobot.Motion;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Protobot.EditorTools {
    // V2: the Override (2026-27) game elements in the parts list (pins, cup, goals, loaders, toggle) remade from VEX's
    // official field CAD (the full-detail field, Assets/Protobot/Field/Override): the original's models were decimated
    // (pins and cups ~32k triangles, round parts faceted). Each official assembly is lined up with the original's model of
    // the same piece (MeshAlign: same size, square turns), so parts in old builds stay where they were. Its meshes are
    // joined into one, a sub-mesh per field material. Tools > Protobot > Build Field Parts.
    public static class FieldPartsBuilder {
        private const string FieldPath = "Assets/Protobot/Field/Override/Override Field.prefab";
        private const string OutFolder = "Assets/Protobot/Field/Override/Parts";
        private const string Prefabs = "Assets/ProtobotRebuilt/Resources/Part Prefabs/1Competition/";

        // prefab, child's first parameter, official assembly, material swaps (pins: every pin has the same halves)
        private static readonly (string prefab, string param, string assembly, (string from, string to)[] swaps)[] Targets = {
            ("Override Pin", "Red Blue", "276-9250-80x_Red-Blue", null),
            ("Override Pin", "Red Yellow", "276-9250-80x_Red-Blue", new[] { ("Field Blue", "Field Yellow") }),
            ("Override Pin", "Blue Yellow", "276-9250-80x_Red-Blue", new[] { ("Field Red", "Field Yellow") }),
            ("Override Pin", "Yellow", "276-9250-80x_Red-Blue", new[] { ("Field Red", "Field Yellow"), ("Field Blue", "Field Yellow") }),
            ("Override Cup", "", "276-9250-81x", null),
            ("Override Goal", "Short", "276-9250-840_Neutral Goal 1", null),
            ("Override Goal", "Tall", "276-9250-840_Neutral Goal 0", null),
            ("Override Goal", "Red Alliance", "276-9250-840_Red 2", null),
            ("Override Goal", "Blue Alliance", "276-9250-840_Blue 2", null),
            ("Override Short Goal", "", "276-9250-840_Neutral Goal 1", null),
            ("Override Tall Goal", "", "276-9250-840_Neutral Goal 0", null),
            ("Override Alliance Goal", "Red", "276-9250-840_Red 2", null),
            ("Override Alliance Goal", "Blue", "276-9250-840_Blue 2", null),
            ("Override Loader", "Red", "276-9250-830_276-9250-831", null),
            ("Override Loader", "Blue", "276-9250-830_276-9250-832", null),
            ("Override Toggle", "Red side", "276-9250-120_Red Alliance Side", null),
        };

        [MenuItem("Tools/Protobot/Build Field Parts")]
        public static void Build() => Debug.Log(Run());

        public static string Run() {
            var field = AssetDatabase.LoadAssetAtPath<GameObject>(FieldPath);
            if (field == null) return "No official field at " + FieldPath + " (Tools > Protobot > Import Field)";
            var root = field.transform.childCount == 1 ? field.transform.GetChild(0) : field.transform;
            if (!AssetDatabase.IsValidFolder(OutFolder)) AssetDatabase.CreateFolder("Assets/Protobot/Field/Override", "Parts");
            var materials = AssetDatabase.FindAssets("t:Material", new[] { "Assets/Protobot/Field/Materials" })
                .Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g))).ToDictionary(m => m.name);
            var report = new List<string>();
            var cache = new Dictionary<string, (Mesh mesh, Material[] mats)>();
            foreach (var group in Targets.GroupBy(t => t.prefab)) {
                var path = Prefabs + group.Key + ".prefab";
                var contents = PrefabUtility.LoadPrefabContents(path);
                try {
                    foreach (var target in group) {
                        var data = contents.GetComponentsInChildren<PartData>(true).FirstOrDefault(d => (d.param1Value ?? "") == target.param);
                        if (data == null) {
                            report.Add(group.Key + " " + target.param + ": no such part");
                            continue;
                        }
                        var assembly = root.Cast<Transform>().FirstOrDefault(c => c.name == target.assembly);
                        if (assembly == null) {
                            report.Add(target.assembly + ": not in the field");
                            continue;
                        }
                        var filter = data.GetComponent<MeshFilter>();
                        var old = filter.sharedMesh;
                        var key = target.assembly + "|" + string.Join(",", (target.swaps ?? new (string, string)[0]).Select(s => s.from + ">" + s.to)) + "|" + old.name;
                        if (!cache.TryGetValue(key, out var made)) {
                            made = Make(assembly, old, target.swaps, materials, group.Key + (target.param != "" ? " " + target.param : ""), out var error);
                            if (made.mesh == null) {
                                report.Add(group.Key + " " + target.param + ": couldn't line up (" + error + ")");
                                continue;
                            }
                            cache[key] = made;
                            report.Add(group.Key + " " + target.param + ": " + Tris(old) + " -> " + Tris(made.mesh) + " triangles, fit " + error.ToString("F3") + "\"");
                        }
                        else report.Add(group.Key + " " + target.param + ": same as above");
                        filter.sharedMesh = made.mesh;
                        if (data.TryGetComponent(out MeshCollider collider)) collider.sharedMesh = made.mesh;
                        data.GetComponent<MeshRenderer>().sharedMaterials = made.mats;
                    }
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
                finally {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
            AssetDatabase.SaveAssets();
            return "[Field parts]\n" + string.Join("\n", report);
        }

        private static long Tris(Mesh m) {
            long n = 0;
            for (var i = 0; i < m.subMeshCount; i++) n += m.GetIndexCount(i) / 3;
            return n;
        }

        // The assembly's meshes as one (a sub-mesh per material, in inches round its middle), lined up with the old
        // model, saved as an asset.
        private static (Mesh mesh, Material[] mats) Make(Transform assembly, Mesh old, (string from, string to)[] swaps,
                                                         Dictionary<string, Material> materials, string name, out float error) {
            var center = Vector3.zero;
            var renderers = assembly.GetComponentsInChildren<MeshRenderer>(true);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            center = bounds.center;
            var byMaterial = new Dictionary<Material, List<(List<Vector3> v, List<Vector3> n, List<int> t)>>();
            foreach (var r in renderers) {
                var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                var matrix = r.transform.localToWorldMatrix;
                var vertices = mesh.vertices;
                var normals = mesh.normals;
                for (var s = 0; s < mesh.subMeshCount; s++) {
                    var material = s < r.sharedMaterials.Length ? r.sharedMaterials[s] : r.sharedMaterials.Last();
                    if (swaps != null)
                        foreach (var (from, to) in swaps)
                            if (material != null && material.name == from && materials.TryGetValue(to, out var swapped)) material = swapped;
                    var indices = mesh.GetIndices(s);
                    var v = new List<Vector3>();
                    var n = new List<Vector3>();
                    var map = new Dictionary<int, int>();
                    var t = new List<int>(indices.Length);
                    foreach (var i in indices) {
                        if (!map.TryGetValue(i, out var j)) {
                            j = v.Count;
                            map[i] = j;
                            v.Add(matrix.MultiplyPoint3x4(vertices[i]) - center);
                            n.Add(normals.Length > i ? matrix.MultiplyVector(normals[i]).normalized : Vector3.up);
                        }
                        t.Add(j);
                    }
                    if (!byMaterial.TryGetValue(material, out var list)) byMaterial[material] = list = new List<(List<Vector3>, List<Vector3>, List<int>)>();
                    list.Add((v, n, t));
                }
            }
            var allV = new List<Vector3>();
            var allN = new List<Vector3>();
            var subs = new List<List<int>>();
            var mats = new List<Material>();
            foreach (var (material, pieces) in byMaterial) {
                var tris = new List<int>();
                foreach (var (v, n, t) in pieces) {
                    var offset = allV.Count;
                    allV.AddRange(v);
                    allN.AddRange(n);
                    foreach (var i in t) tris.Add(i + offset);
                }
                subs.Add(tris);
                mats.Add(material);
            }
            // Square turn and shift onto the old model (the same piece, so old builds keep it in place).
            var (turn, shift, err) = MeshAlign.Find(allV, old.vertices);
            error = err;
            if (err > 0.25f) return (null, null);
            for (var i = 0; i < allV.Count; i++) {
                allV[i] = turn * allV[i] + shift;
                allN[i] = turn * allN[i];
            }
            var made = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            made.SetVertices(allV);
            made.SetNormals(allN);
            made.subMeshCount = subs.Count;
            for (var s = 0; s < subs.Count; s++) made.SetTriangles(subs[s], s);
            made.RecalculateBounds();
            made.RecalculateTangents();
            var assetPath = OutFolder + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing != null) {
                EditorUtility.CopySerialized(made, existing);
                made = existing;
            }
            else AssetDatabase.CreateAsset(made, assetPath);
            return (made, mats.ToArray());
        }
    }
}
