using System;
using System.Collections.Generic;
using System.IO;
using Protobot.Interaction;
using Protobot.Parts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Protobot.Editor
{
    /// <summary>
    /// Tools > Protobot > Build Part Library:
    ///  1. creates one shared URP material per CAD appearance (Aluminum, Steel, Rubber, plastics...);
    ///  2. assigns those materials to every part model by name;
    ///  3. writes the PartCatalog (every part, its bin and real size);
    ///  4. builds a physical prefab and a thumbnail for every part (<see cref="PartPrefabBuilder"/>);
    ///  5. makes the shelf bins in the open scene clickable and tags them with their category.
    /// Reads Assets/Protobot/Parts/Models/parts_manifest.json (Tools/Parts/make_parts_manifest.py).
    /// </summary>
    public static class PartLibraryBuilder
    {
        const string ManifestPath = PartModelImportGuard.PartModelsFolder + "parts_manifest.json";
        const string MaterialFolder = "Assets/Protobot/Parts/Materials/";
        const string CatalogPath = "Assets/Protobot/Parts/PartCatalog.asset";

        [Serializable] class Manifest { public ManifestPart[] parts; }
        [Serializable] class ManifestPart { public string path; public float[] size; public int triangles; public int bodies; public ManifestMaterial[] materials; }
        [Serializable] class ManifestMaterial { public string name; public float[] color; }

        [MenuItem("Tools/Protobot/Build Part Library")]
        public static void Build()
        {
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            var materials = BuildMaterials(manifest);
            AssignMaterials(manifest, materials);
            var catalog = BuildCatalog(manifest);
            PartPrefabBuilder.BuildAll(catalog, false);
            SetupBins();
            Debug.Log($"[Parts] {catalog.parts.Count} parts, {materials.Count} materials.");
        }

        static Dictionary<string, Material> BuildMaterials(Manifest manifest)
        {
            Directory.CreateDirectory(MaterialFolder);
            var result = new Dictionary<string, Material>();
            foreach (var part in manifest.parts)
            {
                foreach (var m in part.materials)
                {
                    if (result.ContainsKey(m.name))
                        continue;
                    var path = MaterialFolder + SafeFileName(m.name) + ".mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null)
                    {
                        material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                        AssetDatabase.CreateAsset(material, path);
                    }
                    PartMaterialLook.Apply(material, m.name, new Color(m.color[0], m.color[1], m.color[2]));
                    EditorUtility.SetDirty(material);
                    result[m.name] = material;
                }
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        static void AssignMaterials(Manifest manifest, Dictionary<string, Material> materials)
        {
            // Not batched with StartAssetEditing: remaps set inside a batch are silently dropped.
            for (var i = 0; i < manifest.parts.Length; i++)
            {
                var part = manifest.parts[i];
                var importer = AssetImporter.GetAtPath(ModelPath(part)) as ModelImporter;
                if (importer == null)
                    continue;
                if (EditorUtility.DisplayCancelableProgressBar("Assigning part materials", part.path, i / (float)manifest.parts.Length))
                    break;
                var changed = false;
                var existing = importer.GetExternalObjectMap();
                foreach (var m in part.materials)
                {
                    var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name);
                    if (existing.TryGetValue(id, out var current) && current == materials[m.name])
                        continue;
                    importer.AddRemap(id, materials[m.name]);
                    changed = true;
                }
                if (changed)
                    importer.SaveAndReimport();
            }
            EditorUtility.ClearProgressBar();
        }

        static PartCatalog BuildCatalog(Manifest manifest)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PartCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<PartCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.parts.Clear();
            foreach (var part in manifest.parts)
            {
                var binId = PartBins.ForLibraryPath(part.path);
                catalog.parts.Add(new PartEntry
                {
                    id = part.path,
                    displayName = PartNaming.DisplayName(part.path),
                    binId = binId,
                    model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(part)),
                    size = new Vector3(part.size[0], part.size[1], part.size[2]),
                    triangles = part.triangles,
                    cuttable = IsStock(binId, part.size),
                });
            }
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        /// <summary>
        /// Metal stock that comes full length and gets cut to size: long, straight pieces from the structure and
        /// shaft bins. Gussets and couplers live in the same bins but are short brackets, not stock.
        /// </summary>
        public static bool IsStock(string binId, float[] size)
        {
            if (binId is not ("c_channels" or "angles" or "plates" or "rails" or "shafts"))
                return false;
            var sorted = new List<float>(size);
            sorted.Sort();
            return sorted[2] >= 0.1f && sorted[2] >= sorted[1] * 3f;
        }

        /// <summary>Adds a PartBin + click collider to each Bin_XX group of the workshop model in the open scene.</summary>
        public static void SetupBins()
        {
            var bins = 0;
            // Collect first: relabelling destroys old label objects while we iterate.
            var binTransforms = new List<Transform>();
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name.StartsWith("Bin_") && t.name.Length == 6)
                    binTransforms.Add(t);

            foreach (var t in binTransforms)
            {
                if (!int.TryParse(t.name.Substring(4), out var modelIndex))
                    continue;
                // The model numbers bins from the back wall outwards; flip each row so labels read left to right.
                var index = modelIndex / 6 * 6 + (5 - modelIndex % 6);
                if (index >= PartBins.All.Count)
                    continue;

                var renderers = t.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                    continue;
                var bounds = renderers[0].bounds;
                foreach (var r in renderers)
                    bounds.Encapsulate(r.bounds);

                // TryGetComponent, not ??: a missing component is a "fake null" object that ?? doesn't catch.
                if (!t.TryGetComponent(out PartBin bin))
                    bin = t.gameObject.AddComponent<PartBin>();
                bin.Configure(PartBins.All[index].id, PartBins.All[index].label);
                if (!t.TryGetComponent(out BoxCollider collider))
                    collider = t.gameObject.AddComponent<BoxCollider>();
                collider.center = t.InverseTransformPoint(bounds.center);
                collider.size = t.InverseTransformVector(bounds.size);
                collider.size = new Vector3(Mathf.Abs(collider.size.x), Mathf.Abs(collider.size.y), Mathf.Abs(collider.size.z));
                AddLabel(t, PartBins.All[index].label);
                if (!t.TryGetComponent(out HoverTarget hover))
                    hover = t.gameObject.AddComponent<HoverTarget>();
                hover.Configure(CursorKind.Point, PartBins.All[index].label, "Click to browse parts");
                EditorUtility.SetDirty(t.gameObject);
                bins++;
            }
            if (bins > 0)
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Parts] Set up {bins} shelf bins.");
        }

        /// <summary>Puts the category name on the bin's paper label, facing into the room.</summary>
        static void AddLabel(Transform bin, string text)
        {
            var plate = bin.Find(bin.name + "_Label");
            if (plate == null || !plate.TryGetComponent(out Renderer plateRenderer))
                return;

            var existing = bin.Find("LabelText");
            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing.gameObject);

            var go = new GameObject("LabelText");
            go.transform.SetParent(bin, false);
            var face = plateRenderer.bounds;
            // The shelf stands against the +X wall, so labels face -X; sit just in front of the paper.
            go.transform.SetPositionAndRotation(new Vector3(face.min.x - 0.001f, face.center.y, face.center.z), Quaternion.LookRotation(Vector3.right));
            var label = go.AddComponent<TMPro.TextMeshPro>();
            label.text = text;
            label.fontSize = 0.25f;
            label.enableAutoSizing = true;
            label.fontSizeMin = 0.08f;
            label.fontSizeMax = 0.3f;
            label.alignment = TMPro.TextAlignmentOptions.Center;
            label.color = new Color(0.1f, 0.1f, 0.1f);
            label.rectTransform.sizeDelta = new Vector2(face.size.z * 0.92f, face.size.y * 0.85f);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }

        static string ModelPath(ManifestPart part) => PartModelImportGuard.PartModelsFolder + part.path + ".fbx";

        static string SafeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }

    /// <summary>
    /// How each CAD appearance name maps to a believable real-world finish. Metals are metallic with
    /// the right sheen, rubber is matte, plastics keep the colour the CAD file gives them.
    /// </summary>
    public static class PartMaterialLook
    {
        public static void Apply(Material material, string appearance, Color cadColor)
        {
            var n = appearance.ToLowerInvariant();
            Color color;
            float metallic, smoothness;
            var transparent = false;

            if ((n.Contains("black") && IsMetal(n)) || (n.Contains("screw") && cadColor.maxColorComponent < 0.3f))
            {
                // Black-oxide screws, anodised black aluminium.
                color = new Color(0.07f, 0.07f, 0.075f);
                metallic = 0.85f;
                smoothness = 0.45f;
            }
            else if (IsAluminum(n))
            {
                color = new Color(0.84f, 0.85f, 0.87f);
                metallic = 1f;
                smoothness = n.Contains("polished") ? 0.85f : 0.6f;
            }
            else if (IsMetal(n) && !n.Contains("brass") && !n.Contains("bronze") && !n.Contains("copper"))
            {
                // Steel, stainless, chrome, zinc-plated hardware, titanium.
                color = n.Contains("chrome") ? new Color(0.8f, 0.8f, 0.82f) : new Color(0.66f, 0.67f, 0.69f);
                metallic = 1f;
                smoothness = n.Contains("polished") || n.Contains("chrome") ? 0.82f : n.Contains("brushed") ? 0.5f : 0.55f;
            }
            else if (n.Contains("brass") || n.Contains("bronze") || n.Contains("copper"))
            {
                color = n.Contains("copper") ? new Color(0.85f, 0.5f, 0.35f) : new Color(0.8f, 0.62f, 0.33f);
                metallic = 1f;
                smoothness = 0.55f;
            }
            else if (n.Contains("rubber") || n.Contains("flex_wheel") || n.Contains("tire") || n.Contains("tread"))
            {
                color = cadColor.maxColorComponent > 0.05f ? cadColor : new Color(0.04f, 0.04f, 0.045f);
                metallic = 0f;
                smoothness = 0.18f;
            }
            else
            {
                // Plastics (nylon, ABS, "Smooth - Red", "210,38,48", ...): keep the CAD colour.
                color = ParseRgbName(n) ?? cadColor;
                metallic = 0f;
                smoothness = n.Contains("glossy") ? 0.75f : n.Contains("nylon") ? 0.35f : 0.45f;
                if (n.Contains("clear") || n.Contains("polycarbonate") || n.Contains("transparent"))
                {
                    transparent = true;
                    color.a = 0.35f;
                    smoothness = 0.85f;
                }
            }

            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.enableInstancing = true;
            if (transparent)
                MakeTransparent(material);
        }

        // Some libraries name it "Alu" or "VEX_Alu"; VEX standoffs are aluminium too.
        static bool IsAluminum(string n) =>
            n.Contains("aluminum") || n.Contains("aluminium") || n.Contains("anodized") || n == "alu" || n.StartsWith("alu_")
            || n.Contains("_alu") || n.Contains("standoff");

        static bool IsMetal(string n) =>
            n.Contains("steel") || n.Contains("stainless") || n.Contains("chrome") || n.Contains("zinc") || n.Contains("titanium")
            || n.Contains("machined") || IsAluminum(n) || n.Contains("brass") || n.Contains("bronze")
            || n.Contains("copper") || (n.Contains("iron") && !n.Contains("gray")) || n == "semi-polished" || n.StartsWith("polished")
            || n.Contains("screw") || n.Contains("vex_metal");

        static void MakeTransparent(Material material)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        static Color? ParseRgbName(string name)
        {
            var parts = name.Split('_')[0].Split(',');
            if (parts.Length != 3)
                return null;
            if (int.TryParse(parts[0], out var r) && int.TryParse(parts[1], out var g) && int.TryParse(parts[2], out var b))
                return new Color(r / 255f, g / 255f, b / 255f);
            return null;
        }
    }
}
