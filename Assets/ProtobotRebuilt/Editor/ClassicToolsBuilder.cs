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

        var saw = AddTool<PartSaw>("Saw", "Saw (cut any part)", "SawIcon.png", "4", -145.5f, sidebar, toggles, keybindings);
        Wire(saw, camera, feedback, red, "planeMaterial");
        var drill = AddTool<PartDrill>("Drill", "Drill (round holes, 1/16\" to 1\")", "DrillIcon.png", "5", -187f, sidebar, toggles, keybindings);
        Wire(drill, camera, feedback, red, "previewMaterial");
        // V2: the measure tool (key 6), with the ruler icon.
        var measure = AddTool<MeasureTool>("Measure", "Measure", "ruler.png", "6", -228f, sidebar, toggles, keybindings);
        var serializedMeasure = new SerializedObject(measure);
        serializedMeasure.FindProperty("feedback").objectReferenceValue = feedback;
        serializedMeasure.ApplyModifiedPropertiesWithoutUndo();
        // V2: the join tool (key 7): face, edge, corner or hole to another part's.
        var join = AddTool<JoinTool>("Join", "Join (face, edge, corner, hole)", "Assets/ProtobotRebuilt/Sprites/V2 Icons/JoinIcon.png", "7", -269.5f, sidebar, toggles, keybindings);
        var serializedJoin = new SerializedObject(join);
        serializedJoin.FindProperty("feedback").objectReferenceValue = feedback;
        serializedJoin.ApplyModifiedPropertiesWithoutUndo();
        BuildPreferences(roots.First(r => r.name == "Preferences Canvas").transform, keybindings);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Classic] Built the Saw and Drill tools.");
    }

    public static T AddTool<T>(string name, string tooltip, string icon, string key, float y,
                                Transform sidebar, Transform toggles, Transform keybindings) where T : MonoBehaviour {
        foreach (var old in new[] { sidebar.Find(name + " Tool"), toggles.Find(name + " Tool Toggle") })
            if (old != null)
                Object.DestroyImmediate(old.gameObject);
        foreach (var old in keybindings.GetComponentsInChildren<InputEvent>(true).Where(e => e.name == "Enable " + name + " Tool").ToList())
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
        iconImage.sprite = LoadSprite(icon.Contains("/") ? icon : IconFolder + icon);
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

    // Preferences > Tools: rebindable keys for the two tools (rows like the General page's) and how the saw works.
    private static void BuildPreferences(Transform canvas, Transform keybindings) {
        const string pageName = "Tools Prefs Menu", tabName = "Tools Toggle";
        var tabs = canvas.Find("Preferences/Category Selection");
        var pages = canvas.Find("Preferences/Category Container");
        foreach (var old in new[] { tabs.Find(tabName), pages.Find(pageName) })
            if (old != null)
                Object.DestroyImmediate(old.gameObject);

        var general = pages.Find("General Prefs Menu");
        var page = Object.Instantiate(general.gameObject, pages);
        page.name = pageName;
        foreach (Transform child in page.transform.Cast<Transform>().ToList())
            if (child.name != "Keybinds Title" && child.name != "Shortcut Container (7)")
                Object.DestroyImmediate(child.gameObject);
        var template = page.transform.Find("Shortcut Container (7)").gameObject;
        var y = -40f;
        foreach (var tool in new[] { "Saw", "Drill", "Measure", "Join" }) {
            var row = Object.Instantiate(template, page.transform);
            row.name = tool + " Shortcut";
            ((RectTransform)row.transform).anchoredPosition = new Vector2(((RectTransform)template.transform).anchoredPosition.x, y);
            y -= 35f;
            var rebind = row.GetComponent<RebindUI>();
            var serialized = new SerializedObject(rebind);
            serialized.FindProperty("inputEvent").objectReferenceValue =
                keybindings.GetComponentsInChildren<InputEvent>(true).First(e => e.name == "Enable " + tool + " Tool");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            row.transform.Find("Shortcut Title").GetComponent<Text>().text = "Enable " + tool + " Tool";
        }
        Object.DestroyImmediate(template);

        // The Game Elements page's help text style, under the rows.
        var helpSource = pages.Find("Game Elements Menu/Game Elements Help");
        var help = Object.Instantiate(helpSource.gameObject, page.transform);
        help.name = "Tools Help";
        var helpRect = (RectTransform)help.transform;
        helpRect.anchoredPosition = new Vector2(helpRect.anchoredPosition.x, y - 10f);
        helpRect.sizeDelta = new Vector2(helpRect.sizeDelta.x, 190f);
        help.GetComponent<Text>().text =
            "Saw: click a part to place a cut plane. Drag the arrow (1/16\" steps, Shift: free) or click the dimension to type " +
            "a distance; drag its dot to measure from a hole, corner, edge or face. R: tilt the plane. Enter cuts, Esc goes back.\n\n" +
            "Drill: pick a bit (1/16\" to 1\") in the tool options, then click a hole.\n\n" +
            "Measure: click two holes, corners, edges or faces to see the distance between them; drag an end to move it.\n\n" +
            "Join: click a face, edge, corner or hole on one part, then on another part: the first moves to meet it.";
        page.SetActive(false);

        var graphicsTab = tabs.Find("Graphics Toggle") ?? tabs.Find("Game Elements Toggle");
        var tab = Object.Instantiate(graphicsTab.gameObject, tabs);
        tab.name = tabName;
        ((RectTransform)tab.transform).anchoredPosition = ((RectTransform)graphicsTab).anchoredPosition + new Vector2(0f, -35f);
        tab.GetComponentInChildren<Text>().text = "Tools";
        var toggle = tab.GetComponent<Toggle>();
        toggle.SetIsOnWithoutNotify(false);
        Clear(toggle.onValueChanged);
        UnityEventTools.AddPersistentListener(toggle.onValueChanged, page.SetActive);
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
