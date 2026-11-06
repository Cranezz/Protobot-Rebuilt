using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Protobot.EditorTools {
    // V2: the competition field (for the multiplayer mode to come). VEX's official field CAD (a SolidWorks STEP from
    // the game page's "Field CAD (ZIP)") is meshed by C:\Dev\PartSources\Field CAD\tools (step_to_glb.py, then
    // glb_to_fbx.py in Blender) into two FBX files in Assets/Protobot/Field/<game>: full detail and a light one (about
    // a fifth of the triangles, for low-end PCs). Tools > Protobot > Import Field sets them to 1 unit = 1 inch and
    // makes a prefab of each with the tiles' top at y = 0 and the field's middle at the origin.
    public static class FieldImporter {
        private const string Folder = "Assets/Protobot/Field";

        [MenuItem("Tools/Protobot/Import Field")]
        public static void Import() {
            var models = AssetDatabase.FindAssets("t:Model", new[] { Folder }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)).ToList();
            if (models.Count == 0) {
                Debug.LogWarning("[Field] No field FBX in " + Folder);
                return;
            }
            foreach (var path in models) {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.useFileScale = true;
                importer.globalScale = 39.37008f;          // the FBX is in metres; the app works in inches
                importer.meshCompression = ModelImporterMeshCompression.Off;
                importer.isReadable = false;               // only drawn (keeps memory down)
                importer.optimizeMeshPolygons = importer.optimizeMeshVertices = true;
                importer.indexFormat = ModelImporterIndexFormat.Auto;
                importer.importNormals = ModelImporterNormals.Import;
                importer.addCollider = false;
                importer.importAnimation = false;
                importer.importCameras = importer.importLights = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                importer.SaveAndReimport();
                MakePrefab(path);
            }
            AssetDatabase.SaveAssets();
        }

        private static void MakePrefab(string modelPath) {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var name = System.IO.Path.GetFileNameWithoutExtension(modelPath);
            var root = new GameObject(name);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            var renderers = instance.GetComponentsInChildren<MeshRenderer>();
            var all = renderers.Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
            // The foam tiles (VEX 276-6904, 24" squares) are the floor: their top is y = 0.
            var tiles = renderers.Where(r => r.name.StartsWith("276-6904")).ToList();
            var floor = tiles.Count > 0 ? tiles.Max(r => r.bounds.max.y) : all.min.y;
            var middle = tiles.Count > 0 ? tiles.Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; }).center : all.center;
            instance.transform.position = new Vector3(-middle.x, -floor, -middle.z);
            foreach (var r in renderers) {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
            }
            var prefabPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(modelPath), name + ".prefab").Replace('\\', '/');
            var triangles = renderers.Sum(r => r.GetComponent<MeshFilter>().sharedMesh is Mesh m ? Enumerable.Range(0, m.subMeshCount).Sum(i => (long)m.GetIndexCount(i)) / 3 : 0);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[Field] {name}: {renderers.Length} parts, {triangles / 1e6:0.0}M triangles, {all.size.x:0.#} x {all.size.z:0.#} x {all.size.y:0.#} in, tiles {tiles.Count} -> {prefabPath}");
        }
    }
}
