using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Protobot.EditorTools {
    // V2: cleaned-up copies of a few of the original's models (the originals are left untouched; the part prefabs point at
    // the copies in Models/Optimized): the V5 motor without its hidden insides (gears, board: 43k -> ~13k triangles,
    // looks the same), and the 2.25" and 2.5" screws without modelled threads (19k triangles each; every other length is
    // a plain ~1.8k-triangle screw): the 2" screw's model with its shank lengthened to the exact length.
    // Tools > Protobot > Clean Up Original Models.
    public static class ModelCleanup {
        private const string Folder = "Assets/ProtobotRebuilt/Models/Optimized";
        private const string ScrewPrefab = "Assets/ProtobotRebuilt/Resources/Part Prefabs/5Structure/Screw.prefab";

        [MenuItem("Tools/Protobot/Clean Up Original Models")]
        public static void Run() {
            Debug.Log("[Cleanup] " + Motor() + "; " + LongScrews());
        }

        public static string Motor() {
            var source = AssetDatabase.LoadAllAssetsAtPath("Assets/ProtobotRebuilt/Models/Electronics/Motor.fbx").OfType<Mesh>().First(m => m.name == "MOTR");
            (int before, int after) counts = default;
            var copy = SaveCopy(source, "MOTR (outside only)", m => counts = HiddenGeometry.Strip(m));
            var replaced = Replace("Assets/ProtobotRebuilt/Resources/Part Prefabs/2Electronics/Motor.prefab", source, copy)
                           + Replace("Assets/ProtobotRebuilt/Prefabs/Parts.prefab", source, copy);
            return $"motor {counts.before} -> {copy.triangles.Length / 3} triangles ({replaced} references)";
        }

        public static string LongScrews() {
            var screws = AssetDatabase.LoadAllAssetsAtPath("Assets/ProtobotRebuilt/Models/Structure/Screws.fbx").OfType<Mesh>().ToList();
            var twoInch = screws.First(m => m.name == "SCRW 2.00in");
            var root = PrefabUtility.LoadPrefabContents(ScrewPrefab);
            var report = "";
            try {
                // Where the 2" screw sits in the prefab (its frame) and where each threaded one sits.
                var reference = root.GetComponentsInChildren<MeshFilter>(true).First(f => f.sharedMesh == twoInch);
                foreach (var (file, extra) in new[] { ("2.25in screw.obj", 0.25f), ("2.50in screw.obj", 0.5f) }) {
                    var threaded = AssetDatabase.LoadAllAssetsAtPath("Assets/ProtobotRebuilt/Models/Structure/" + file).OfType<Mesh>().First();
                    var users = root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh == threaded).ToList();
                    if (users.Count == 0) continue;
                    var target = users[0].transform;
                    var made = SaveCopy(twoInch, "SCRW " + (2f + extra).ToString("0.00") + "in (plain)", m => Lengthen(m, reference.transform, target, extra, threaded));
                    foreach (var f in users) f.sharedMesh = made;
                    foreach (var c in root.GetComponentsInChildren<MeshCollider>(true).Where(c => c.sharedMesh == threaded)) c.sharedMesh = made;
                    report += $"{file} {threaded.triangles.Length / 3} -> {made.triangles.Length / 3} triangles (length {Length(made, target):0.000}\" vs {Length(threaded, target):0.000}\"); ";
                }
                PrefabUtility.SaveAsPrefabAsset(root, ScrewPrefab);
            }
            finally {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return report;
        }

        // The 2" screw's mesh, put into the threaded screw's frame, its shank (everything past the head) moved
        // `extra` inches further along the screw.
        private static void Lengthen(Mesh mesh, Transform from, Transform to, float extra, Mesh threaded) {
            var toRoot = from.localToWorldMatrix;
            var back = to.worldToLocalMatrix;
            var points = mesh.vertices.Select(v => toRoot.MultiplyPoint3x4(v)).ToArray();
            // The screw's axis: the longest side of its box; the head is the end that's wider.
            var b = new Bounds(points[0], Vector3.zero);
            foreach (var p in points) b.Encapsulate(p);
            var axis = b.size.x >= b.size.y && b.size.x >= b.size.z ? Vector3.right : b.size.y >= b.size.z ? Vector3.up : Vector3.forward;
            float lo = points.Min(p => Vector3.Dot(p, axis)), hi = points.Max(p => Vector3.Dot(p, axis));
            float Radius(float end) => points.Where(p => Mathf.Abs(Vector3.Dot(p, axis) - end) < 0.05f)
                .Select(p => Vector3.ProjectOnPlane(p - b.center, axis).magnitude).DefaultIfEmpty(0f).Max();
            var headAtLo = Radius(lo) > Radius(hi);
            var head = headAtLo ? lo : hi;
            var toward = headAtLo ? axis : -axis;
            // Past the head (0.2" from its end: the head of an 8-32 VEX screw is under 0.2" tall) the shank moves.
            for (var i = 0; i < points.Length; i++) {
                var along = (Vector3.Dot(points[i], axis) - head) * (headAtLo ? 1f : -1f);
                if (along > 0.2f) points[i] += toward * extra;
            }
            // Head on the threaded screw's head, centred on its axis (in case the two sit apart in the prefab).
            var real = threaded.vertices.Select(v => to.localToWorldMatrix.MultiplyPoint3x4(v)).ToArray();
            var rb = new Bounds(real[0], Vector3.zero);
            foreach (var p in real) rb.Encapsulate(p);
            float rlo = real.Min(p => Vector3.Dot(p, axis)), rhi = real.Max(p => Vector3.Dot(p, axis));
            float RealRadius(float end) => real.Where(p => Mathf.Abs(Vector3.Dot(p, axis) - end) < 0.05f)
                .Select(p => Vector3.ProjectOnPlane(p - rb.center, axis).magnitude).DefaultIfEmpty(0f).Max();
            var realHead = RealRadius(rlo) > RealRadius(rhi) ? rlo : rhi;
            var nb = new Bounds(points[0], Vector3.zero);
            foreach (var p in points) nb.Encapsulate(p);
            var shift = axis * (realHead - head) + Vector3.ProjectOnPlane(rb.center - nb.center, axis);
            for (var i = 0; i < points.Length; i++) points[i] += shift;
            mesh.vertices = points.Select(p => back.MultiplyPoint3x4(p)).ToArray();
            var normalMatrix = back * toRoot;
            mesh.normals = mesh.normals.Select(n => normalMatrix.MultiplyVector(n).normalized).ToArray();
            mesh.RecalculateBounds();
        }

        private static float Length(Mesh mesh, Transform frame) {
            var m = frame.localToWorldMatrix;
            var points = mesh.vertices.Select(v => m.MultiplyPoint3x4(v)).ToArray();
            var b = new Bounds(points[0], Vector3.zero);
            foreach (var p in points) b.Encapsulate(p);
            return Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        }

        private static Mesh SaveCopy(Mesh source, string name, System.Action<Mesh> change) {
            Directory.CreateDirectory(Folder);
            var path = Folder + "/" + name + ".asset";
            var copy = Object.Instantiate(source);
            copy.name = source.name;
            change(copy);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) {
                EditorUtility.CopySerialized(copy, existing);
                Object.DestroyImmediate(copy);
                AssetDatabase.SaveAssets();
                return existing;
            }
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();
            return copy;
        }

        private static int Replace(string prefabPath, Mesh from, Mesh to) {
            if (!File.Exists(prefabPath)) return 0;
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            var n = 0;
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh == from)) {
                f.sharedMesh = to;
                n++;
            }
            foreach (var c in root.GetComponentsInChildren<MeshCollider>(true).Where(c => c.sharedMesh == from)) {
                c.sharedMesh = to;
                n++;
            }
            if (n > 0) PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            return n;
        }
    }
}
