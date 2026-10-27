using System;
using System.Linq;
using Protobot;
using Protobot.InputEvents;
using Protobot.Tools;
using Protobot.UI;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// V2: adds the Saw and Drill to the Tools sidebar, set up like the original tools: a sidebar toggle (copied from
// the Move tool's), a ToolToggle under "Tool Toggles" that switches the tool's component on and off, and a
// keybind (InputEvent under "Keybindings"). Run again to rebuild them.
public static class ClassicToolsBuilder {
    private const string ScenePath = "Assets/ProtobotRebuilt/Scenes/Main.unity";
    private const string IconFolder = "Assets/ProtobotRebuilt/Sprites/Tools Icons/";

    [MenuItem("Tools/Protobot/Build Saw and Drill Tools")]
    public static void Build() {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath);

        var roots = scene.GetRootGameObjects();
        var sidebar = roots.First(r => r.name == "Build Canvas").transform.Find("Tools Sidebar");
        var toggles = roots.First(r => r.name == "Tool Toggles").transform;
        var keybindings = roots.First(r => r.name == "Keybindings").transform;
        var camera = roots.SelectMany(r => r.GetComponentsInChildren<MouseCast>(true)).First(c => c.name == "Main Camera");
        var feedback = roots.SelectMany(r => r.GetComponentsInChildren<FeedbackDisplay>(true)).First();
        var red = AssetDatabase.LoadAssetAtPath<Material>("Assets/ProtobotRebuilt/Materials/Pure Red.mat");

        var saw = AddTool<PartSaw>("Saw", "Saw (cut between holes)", "SawIcon.png", "4", -145.5f, sidebar, toggles, keybindings);
        Wire(saw, camera, feedback, red, "planeMaterial");
        var drill = AddTool<PartDrill>("Drill", "Drill (1/4\" round hole)", "DrillIcon.png", "5", -187f, sidebar, toggles, keybindings);
        Wire(drill, camera, feedback, red, "previewMaterial");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Classic] Built the Saw and Drill tools.");
    }

    private static T AddTool<T>(string name, string tooltip, string icon, string key, float y,
                                Transform sidebar, Transform toggles, Transform keybindings) where T : MonoBehaviour {
        foreach (var old in new[] { sidebar.Find(name + " Tool"), toggles.Find(name + " Tool Toggle"), keybindings.Find("Enable " + name + " Tool") })
            if (old != null)
                Object.DestroyImmediate(old.gameObject);

        // Keybind: a copy of "Enable Move Tool" on another key.
        var moveKey = keybindings.GetComponentsInChildren<InputEvent>(true).First(e => e.name == "Enable Move Tool");
        var keyEvent = Object.Instantiate(moveKey.gameObject, moveKey.transform.parent).GetComponent<InputEvent>();
        keyEvent.name = "Enable " + name + " Tool";
        var serializedKey = new SerializedObject(keyEvent);
        var action = serializedKey.FindProperty("defaultAction");
        action.FindPropertyRelative("m_Name").stringValue = keyEvent.name;
        action.FindPropertyRelative("m_Id").stringValue = Guid.NewGuid().ToString();
        var bindings = action.FindPropertyRelative("m_SingletonActionBindings");
        for (var i = 0; i < bindings.arraySize; i++) {
            var binding = bindings.GetArrayElementAtIndex(i);
            binding.FindPropertyRelative("m_Id").stringValue = Guid.NewGuid().ToString();
            binding.FindPropertyRelative("m_Action").stringValue = keyEvent.name;
            var path = binding.FindPropertyRelative("m_Path");
            if (path.stringValue.StartsWith("<Keyboard>/"))
                path.stringValue = "<Keyboard>/" + key;
        }
        serializedKey.ApplyModifiedPropertiesWithoutUndo();

        // Sidebar button: a copy of the Move tool's.
        var moveButton = sidebar.Find("Move Tool");
        var button = Object.Instantiate(moveButton.gameObject, sidebar);
        button.name = name + " Tool";
        ((RectTransform)button.transform).anchoredPosition = new Vector2(((RectTransform)moveButton).anchoredPosition.x, y);
        var iconImage = button.transform.Find("Move Icon").GetComponent<Image>();
        iconImage.name = name + " Icon";
        iconImage.sprite = LoadSprite(IconFolder + icon);
        button.GetComponent<Tooltip>().text = tooltip;
        var buttonToggle = button.GetComponent<Toggle>();
        buttonToggle.SetIsOnWithoutNotify(false);

        // Tool toggle: a copy of the Move tool's, without its "something must be selected" requirement.
        var moveToggle = toggles.Find("Move Tool Toggle");
        var toggleObject = Object.Instantiate(moveToggle.gameObject, toggles);
        toggleObject.name = name + " Tool Toggle";
        foreach (var requirement in toggleObject.GetComponents<TagToolRequirement>())
            Object.DestroyImmediate(requirement);
        var toolToggle = toggleObject.GetComponent<ToolToggle>();
        var tool = toggleObject.AddComponent<T>();
        tool.enabled = false;

        var serializedToggle = new SerializedObject(toolToggle);
        serializedToggle.FindProperty("toggleOnInput").objectReferenceValue = keyEvent;
        serializedToggle.ApplyModifiedPropertiesWithoutUndo();
        Clear(toolToggle.OnToggle);
        Clear(toolToggle.OnActivate);
        Clear(toolToggle.OnDeactivate);
        UnityEventTools.AddBoolPersistentListener(toolToggle.OnToggle, IsOnSetter(buttonToggle), true);
        UnityEventTools.AddBoolPersistentListener(toolToggle.OnActivate, Setter(tool), true);
        UnityEventTools.AddBoolPersistentListener(toolToggle.OnDeactivate, Setter(tool), false);

        Clear(buttonToggle.onValueChanged);
        UnityEventTools.AddPersistentListener(buttonToggle.onValueChanged, toolToggle.Toggle);
        return tool;
    }

    private static UnityAction<bool> Setter(Behaviour behaviour) =>
        (UnityAction<bool>)Delegate.CreateDelegate(typeof(UnityAction<bool>), behaviour, "set_enabled");

    // The other tools' OnToggle turns their sidebar button on (Toggle.isOn).
    private static UnityAction<bool> IsOnSetter(Toggle toggle) =>
        (UnityAction<bool>)Delegate.CreateDelegate(typeof(UnityAction<bool>), toggle, "set_isOn");

    private static Sprite LoadSprite(string path) {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single) {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void Clear(UnityEventBase unityEvent) {
        while (unityEvent.GetPersistentEventCount() > 0)
            UnityEventTools.RemovePersistentListener(unityEvent, 0);
    }

    private static void Wire(MonoBehaviour tool, MouseCast camera, FeedbackDisplay feedback, Material material, string materialField) {
        var serialized = new SerializedObject(tool);
        serialized.FindProperty("mouseCast").objectReferenceValue = camera;
        serialized.FindProperty("feedback").objectReferenceValue = feedback;
        serialized.FindProperty(materialField).objectReferenceValue = material;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
