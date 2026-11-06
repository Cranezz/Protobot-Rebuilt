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
        private const string MaterialsFolder = Folder + "/Materials";

        // The field's colors, from VEX's renders and drawings of the Override field (game manual, Appendix A) and photos:
        // mid-gray foam tiles in two shades, white tape, clear polycarbonate wall panels on aluminium rails with black
        // fittings, black neutral goals, red/blue alliance goals, red/blue/yellow pins, cups dark gray over clear.
        // The app lights everything with a bright flat ambient (1.63) plus a sun, so a color shows about twice as
        // bright as its value: whites are ~0.42 (white plastic, not glare), and everything is matte (low smoothness).
        // glb_to_fbx.py names the FBX materials "Field <kind>"; Import Field maps them onto these material assets,
        // in Assets/Protobot/Field/Materials (re-running Import Field sets them to the values below: tune them here).
        private static readonly (string kind, Color color, float smoothness, float metallic)[] Palette = {
            ("Tile", new Color(0.26f, 0.26f, 0.265f), 0.05f, 0f),
            ("Tile Dark", new Color(0.235f, 0.235f, 0.24f), 0.05f, 0f),
            ("Tape", new Color(0.41f, 0.41f, 0.40f), 0.15f, 0f),
            ("Panel", new Color(0.70f, 0.74f, 0.76f, 0.22f), 0.2f, 0f),
            ("Rail", new Color(0.62f, 0.63f, 0.65f), 0.25f, 0.5f),
            ("Fitting", new Color(0.06f, 0.06f, 0.065f), 0.2f, 0f),
            ("Rubber", new Color(0.05f, 0.05f, 0.05f), 0.05f, 0f),
            ("Hardware", new Color(0.75f, 0.76f, 0.77f), 0.45f, 0.75f),
            ("Red", new Color(0.42f, 0.04f, 0.08f), 0.25f, 0f),
            ("Blue", new Color(0.0f, 0.26f, 0.42f), 0.25f, 0f),
            ("Yellow", new Color(0.50f, 0.42f, 0.03f), 0.25f, 0f),
            ("Goal Black", new Color(0.06f, 0.06f, 0.065f), 0.25f, 0f),
            ("Dark Gray", new Color(0.15f, 0.155f, 0.16f), 0.2f, 0f),
            ("Clear", new Color(0.72f, 0.76f, 0.80f, 0.25f), 0.25f, 0f),
            ("AprilTag", new Color(0.2f, 0.2f, 0.2f), 0.1f, 0f),
            ("Gray", new Color(0.25f, 0.25f, 0.26f), 0.2f, 0f),
        };

        /// <summary>The field's material assets, made or brought up to the palette.</summary>
        private static System.Collections.Generic.Dictionary<string, Material> Materials() {
            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
                AssetDatabase.CreateFolder(Folder, "Materials");
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var result = new System.Collections.Generic.Dictionary<string, Material>();
            foreach (var (kind, color, smoothness, metallic) in Palette) {
                var path = $"{MaterialsFolder}/Field {kind}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) {
                    material = new Material(lit) { name = "Field " + kind };
                    AssetDatabase.CreateAsset(material, path);
                }
                material.SetColor("_BaseColor", color);
                material.SetFloat("_Smoothness", smoothness);
                material.SetFloat("_Metallic", metallic);
                var transparent = color.a < 0.99f;
                material.SetFloat("_Surface", transparent ? 1f : 0f);
                material.SetFloat("_Blend", 0f);
                // Plain alpha blending: "preserve specular" kept the sky's full reflection on clear panels at a
                // glancing angle, which showed them as white sheets.
                material.SetFloat("_BlendModePreserveSpecular", 0f);
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.SetFloat("_ZWrite", transparent ? 0f : 1f);
                material.SetFloat("_SrcBlend", transparent ? (float)UnityEngine.Rendering.BlendMode.SrcAlpha : (float)UnityEngine.Rendering.BlendMode.One);
                material.SetFloat("_DstBlend", transparent ? (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : (float)UnityEngine.Rendering.BlendMode.Zero);
                material.renderQueue = transparent ? (int)UnityEngine.Rendering.RenderQueue.Transparent : -1;
                if (transparent) material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                else material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", transparent ? "Transparent" : "Opaque");
                EditorUtility.SetDirty(material);
                result["Field " + kind] = material;
            }
            return result;
        }

        [MenuItem("Tools/Protobot/Import Field")]
        public static void Import() {
            var models = AssetDatabase.FindAssets("t:Model", new[] { Folder }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)).ToList();
            if (models.Count == 0) {
                Debug.LogWarning("[Field] No field FBX in " + Folder);
                return;
            }
            var materials = Materials();
            foreach (var path in models) {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                // The FBX's own materials are replaced by the palette's assets, matched by name.
                foreach (var pair in materials)
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
                importer.useFileScale = true;
                importer.globalScale = 39.37008f;          // the FBX is in metres; the app works in inches
                importer.meshCompression = ModelImporterMeshCompression.Off;
                // The light field's meshes are also the game's collision shapes (goals, loaders, pins, cups: Scripts/Game),
                // so they stay readable; the full one is only drawn (keeps memory down).
                importer.isReadable = path.Contains("(light)");
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
