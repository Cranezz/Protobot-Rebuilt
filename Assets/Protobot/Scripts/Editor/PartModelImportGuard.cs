using UnityEditor;
using UnityEngine;

namespace Protobot.Editor
{
    /// <summary>
    /// Locks the import settings of every VEX part model so their real-world
    /// dimensions can't be changed by accident in the Inspector.
    /// Applies to anything inside Assets/Protobot/Parts/Models.
    /// </summary>
    public class PartModelImportGuard : AssetPostprocessor
    {
        public const string PartModelsFolder = "Assets/Protobot/Parts/Models/";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(PartModelsFolder))
                return;

            var importer = (ModelImporter)assetImporter;

            // The FBX files are written in metres by Tools/Blender/convert_parts.py, which also checks
            // every part against the size Fusion measured. Keep that size exactly.
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;

            // Compression rounds vertex positions, which would shift hole positions.
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.optimizeMeshVertices = true;
            importer.optimizeMeshPolygons = true;
            importer.importNormals = ModelImporterNormals.Import;

            // The saw and drill reshape metal structure parts and shafts on the CPU, so those meshes stay
            // readable. Everything else skips the extra memory.
            importer.isReadable = IsEditable(assetPath);

            // Parts are static meshes: no rigs, animations or cameras/lights from the file.
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        }

        public static bool IsEditable(string modelPath) =>
            modelPath.StartsWith(PartModelsFolder + "Structure/") || modelPath.StartsWith(PartModelsFolder + "Motion/Shaft");

        // Screen-height thresholds for the LOD levels written by convert_parts.py (_LOD0/_LOD1/_LOD2).
        // The graphics preset's Model Detail scales these through QualitySettings.lodBias.
        static readonly float[] LodTransitions = { 0.15f, 0.04f, 0.002f };

        void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith(PartModelsFolder) || !root.TryGetComponent(out LODGroup group))
                return;

            var lods = group.GetLODs();
            for (var i = 0; i < lods.Length; i++)
                lods[i].screenRelativeTransitionHeight = i == lods.Length - 1 ? LodTransitions[^1] : LodTransitions[Mathf.Min(i, LodTransitions.Length - 2)];
            group.SetLODs(lods);
            group.fadeMode = LODFadeMode.CrossFade;
            group.animateCrossFading = true;
        }
    }
}
