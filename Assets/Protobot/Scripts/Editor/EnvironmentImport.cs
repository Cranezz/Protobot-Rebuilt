using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Protobot.Editor
{
    /// <summary>
    /// Brings the Blender-built workshop into Unity.
    /// Import settings are applied automatically; "Tools > Protobot > Build Workshop Materials" turns
    /// Workshop_materials.json (written by Tools/Blender/workshop_export.py) into URP materials and
    /// assigns them to the FBX by material name.
    /// </summary>
    public class EnvironmentImport : AssetPostprocessor
    {
        const string EnvironmentRoot = "Assets/Protobot/Art/Environment/";
        const string TextureFolder = EnvironmentRoot + "Textures/";
        const string MaterialFolder = EnvironmentRoot + "Materials/";
        const string WorkshopModel = EnvironmentRoot + "Models/Workshop.fbx";
        const string WorkshopManifest = EnvironmentRoot + "Models/Workshop_materials.json";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(TextureFolder))
                return;

            var importer = (TextureImporter)assetImporter;
            var file = Path.GetFileNameWithoutExtension(assetPath);
            importer.maxTextureSize = 2048;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;

            if (file.EndsWith("_nor_gl"))
                importer.textureType = TextureImporterType.NormalMap;
            else if (file.EndsWith("_rough") || file.EndsWith("_ao") || file.EndsWith("_ms") || file.EndsWith("_metal"))
                importer.sRGBTexture = false;
        }

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(EnvironmentRoot))
                return;

            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.generateSecondaryUV = true;  // lightmap UVs for baked lighting
            importer.bakeAxisConversion = true;   // Blender is Z-up: bake the conversion so every object has a clean transform
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        }

        [Serializable]
        class Manifest
        {
            public MaterialEntry[] materials;
        }

        [Serializable]
        class MaterialEntry
        {
            public string name;
            public string texture;
            public float[] color;
            public float roughness;
            public float metallic;
            public float[] emission;
            public float emissionStrength;
            public float alpha;
        }

        [MenuItem("Tools/Protobot/Build Workshop Materials")]
        public static void BuildWorkshopMaterials()
        {
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(WorkshopManifest));
            Directory.CreateDirectory(MaterialFolder);

            var importer = (ModelImporter)AssetImporter.GetAtPath(WorkshopModel);
            foreach (var entry in manifest.materials)
            {
                var material = CreateOrUpdateMaterial(entry);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), entry.name), material);
            }

            importer.SaveAndReimport();
            Debug.Log($"[Workshop] Built {manifest.materials.Length} materials and assigned them to Workshop.fbx");
        }

        static Material CreateOrUpdateMaterial(MaterialEntry entry)
        {
            var path = MaterialFolder + entry.name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            var color = new Color(entry.color[0], entry.color[1], entry.color[2], entry.alpha);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", entry.metallic);
            material.SetFloat("_Smoothness", 1f - entry.roughness);

            if (!string.IsNullOrEmpty(entry.texture))
                ApplyTextureSet(material, entry.texture);

            if (entry.emission != null && entry.emission.Length == 3 && entry.emissionStrength > 0f)
            {
                // Blender's emission strength is much hotter than Unity's; scale it to a sensible HDR value.
                var emission = new Color(entry.emission[0], entry.emission[1], entry.emission[2]) * (entry.emissionStrength * 0.35f);
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }

            if (entry.alpha < 0.999f)
                MakeTransparent(material);

            EditorUtility.SetDirty(material);
            return material;
        }

        static void ApplyTextureSet(Material material, string id)
        {
            var folder = TextureFolder + id + "/";
            Texture2D Find(string suffix)
            {
                foreach (var ext in new[] { ".jpg", ".png" })
                {
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + id + suffix + ext);
                    if (tex != null)
                        return tex;
                }
                return null;
            }

            material.SetTexture("_BaseMap", Find("_diff"));

            var normal = Find("_nor_gl");
            material.SetTexture("_BumpMap", normal);
            SetKeyword(material, "_NORMALMAP", normal != null);

            var occlusion = Find("_ao");
            material.SetTexture("_OcclusionMap", occlusion);
            SetKeyword(material, "_OCCLUSIONMAP", occlusion != null);

            var metallicSmoothness = Find("_ms");
            material.SetTexture("_MetallicGlossMap", metallicSmoothness);
            SetKeyword(material, "_METALLICSPECGLOSSMAP", metallicSmoothness != null);
            if (metallicSmoothness != null)
            {
                material.SetFloat("_SmoothnessTextureChannel", 0f);  // smoothness from the metallic map's alpha
                material.SetFloat("_Smoothness", 1f);
            }
        }

        static void MakeTransparent(Material material)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetShaderPassEnabled("ShadowCaster", false);
        }

        static void SetKeyword(Material material, string keyword, bool on)
        {
            if (on)
                material.EnableKeyword(keyword);
            else
                material.DisableKeyword(keyword);
        }
    }
}
