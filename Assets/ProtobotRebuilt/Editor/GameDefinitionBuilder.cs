using UnityEditor;
using UnityEngine;
using Protobot.Game;

// V2: makes the game definitions (Resources/Games) the app loads its fields from. Run again after Import Field.
public static class GameDefinitionBuilder {
    private const string Folder = "Assets/ProtobotRebuilt/Resources/Games";

    [MenuItem("Tools/Protobot/Build Game Definitions")]
    public static void Build() {
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/ProtobotRebuilt/Resources", "Games");
        var path = Folder + "/Override 2026-27.asset";
        var definition = AssetDatabase.LoadAssetAtPath<GameDefinition>(path);
        if (definition == null) {
            definition = ScriptableObject.CreateInstance<GameDefinition>();
            AssetDatabase.CreateAsset(definition, path);
        }
        definition.title = "Override";
        definition.season = "2026-27";
        definition.program = "V5RC";
        definition.field = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Protobot/Field/Override/Override Field.prefab");
        definition.fieldLight = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Protobot/Field/Override/Override Field (light).prefab");
        definition.skillsSeconds = 60f;
        EditorUtility.SetDirty(definition);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Game] {definition.Name}: field {(definition.field != null)}, light {(definition.fieldLight != null)} -> {path}");
    }
}
