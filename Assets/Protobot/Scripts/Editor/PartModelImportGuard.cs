using UnityEditor;

namespace Protobot.Editor
{
    /// <summary>
    /// Locks the import settings of every VEX part model so their real-world
    /// dimensions can't be changed by accident in the Inspector.
    /// Applies to anything inside Assets/Protobot/Parts/Models.
    /// </summary>
    public class PartModelImportGuard : AssetPostprocessor
    {
        const string PartModelsFolder = "Assets/Protobot/Parts/Models/";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(PartModelsFolder))
                return;

            var importer = (ModelImporter)assetImporter;

            // Keep the size exactly as authored in the source file.
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = false;

            // Compression rounds vertex positions, which would shift hole positions.
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.optimizeMeshVertices = true;
            importer.optimizeMeshPolygons = true;

            // Parts are static meshes: no rigs, animations or cameras/lights from the file.
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
        }
    }
}
