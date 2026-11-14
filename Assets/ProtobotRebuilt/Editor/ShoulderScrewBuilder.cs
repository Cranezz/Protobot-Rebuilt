using Parts_List;
using Protobot;
using UnityEditor;
using UnityEngine;

namespace Protobot.EditorTools {
    /// <summary>
    /// V2: RoboSource's 2.50" shoulder screw with a 0.04" shoulder (brown head), which VEX-CAD has no model of: made from
    /// VEX-CAD's 2.00" one (0.04" shoulder) with its thread 0.5" longer. The model's thread is a plain cylinder from
    /// the tip (z = 0, the chamfer to -0.024) back to the shoulder (-1.96), so moving the tip end 0.5" further out
    /// lengthens it exactly; the shoulder and head stay as they are. The VEX models themselves aren't touched: the mesh
    /// is a separate asset in Models/V2. Added to the Hardware family by Tools > Protobot > Add 2.5in Shoulder Screw,
    /// and by Import VEX Parts each time it rebuilds that family.
    /// </summary>
    public static class ShoulderScrewBuilder {
        private const string FamilyPrefab = "Assets/ProtobotRebuilt/Resources/Part Prefabs/VEX/Hardware.prefab";
        private const string Source = "2.00\" Shoulder Screw (0.04\" shoulder)";
        public const string Name = "2.50\" Shoulder Screw (0.04\" shoulder)";
        private const string MeshPath = "Assets/ProtobotRebuilt/Models/V2/Shoulder Screw 2.50in x 0.04in.asset";
        private const float Extra = 0.5f;
        // 0.5" more of an 8-32 steel shank (0.164" across, 7.85 g/cm³).
        private static readonly float ExtraGrams = Mathf.PI * 0.082f * 0.082f * Extra * 16.387f * 7.85f;

        [MenuItem("Tools/Protobot/Add 2.5in Shoulder Screw")]
        public static void AddFromMenu() {
            var made = Add(FamilyPrefab);
            Debug.Log(made ? "[ShoulderScrew] added " + Name : "[ShoulderScrew] couldn't add it (see the warnings)");
        }

        /// <summary>Adds (or remakes) the 2.50" shoulder screw in a Hardware family prefab; false when it couldn't.</summary>
        public static bool Add(string prefabPath) {
            if (!prefabPath.Replace('\\', '/').EndsWith("/Hardware.prefab")) return false;
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try {
                var source = root.transform.Find(Source);
                if (source == null) {
                    Debug.LogWarning("[ShoulderScrew] no \"" + Source + "\" in " + prefabPath);
                    return false;
                }
                var old = root.transform.Find(Name);
                if (old != null) Object.DestroyImmediate(old.gameObject);

                var sourceMesh = source.GetComponent<MeshFilter>().sharedMesh;
                var mesh = Object.Instantiate(sourceMesh);
                mesh.name = Name;
                var vertices = mesh.vertices;
                for (var i = 0; i < vertices.Length; i++)
                    if (vertices[i].z > -1f) vertices[i].z += Extra;   // the tip end (the thread has no points in between)
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(MeshPath));
                AssetDatabase.DeleteAsset(MeshPath);
                AssetDatabase.CreateAsset(mesh, MeshPath);

                var copy = Object.Instantiate(source.gameObject, root.transform);
                copy.name = Name;
                copy.transform.SetSiblingIndex(source.GetSiblingIndex() + 1);
                copy.GetComponent<MeshFilter>().sharedMesh = mesh;
                if (copy.TryGetComponent(out MeshCollider meshCollider)) meshCollider.sharedMesh = mesh;
                var data = copy.GetComponent<PartData>();
                data.param1Value = Name;
                if (copy.TryGetComponent(out PartName partName)) {
                    partName.name = Name;
                    partName.weightInGrams += ExtraGrams;
                }
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                return true;
            }
            finally {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
