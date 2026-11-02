using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// V2: adds the new interface (Protobot.Interface.Interface: tool rail, tool options, overlay, tooltips) to the
// main scene with its fonts and icons. Run Tools > Protobot > Build Saw and Drill Tools first (it adds the
// measure tool the rail shows). Run again to rebuild.
public static class InterfaceBuilder {
    private const string ScenePath = "Assets/ProtobotRebuilt/Scenes/Main.unity";
    private const string Fonts = "Assets/ProtobotRebuilt/Fonts/Roboto/";
    private const string Icons = "Assets/ProtobotRebuilt/Sprites/";

    [MenuItem("Tools/Protobot/Build V2 Interface")]
    public static void Build() {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath);

        foreach (var old in scene.GetRootGameObjects().Where(r => r.name == "V2 Interface"))
            Object.DestroyImmediate(old);

        var root = new GameObject("V2 Interface", typeof(RectTransform));
        root.layer = 5;
        var host = root.AddComponent<Protobot.Interface.Interface>();
        // Next to the original UI in the hierarchy.
        var buildCanvas = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "Build Canvas");
        if (buildCanvas != null)
            root.transform.SetSiblingIndex(buildCanvas.transform.GetSiblingIndex() + 1);

        var serialized = new SerializedObject(host);
        serialized.FindProperty("regular").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Fonts + "Roboto-Regular SDF.asset");
        serialized.FindProperty("medium").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Fonts + "Roboto-Medium SDF.asset");
        serialized.FindProperty("bold").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Fonts + "Roboto-Bold SDF.asset");
        serialized.FindProperty("measureIcon").objectReferenceValue = Sprite(Icons + "Tools Icons/ruler.png");
        serialized.FindProperty("chevronIcon").objectReferenceValue = Sprite(Icons + "Tools Icons/chevron-arrow-up.png");
        serialized.FindProperty("searchIcon").objectReferenceValue = Sprite(Icons + "Menu UI/zoom-in.png");
        serialized.FindProperty("closeIcon").objectReferenceValue = Sprite(Icons + "Tools Icons/minimize.png");
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[V2] Built the new interface.");
    }

    private static Sprite Sprite(string path) {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            return null;
        if (importer.textureType != TextureImporterType.Sprite) {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
