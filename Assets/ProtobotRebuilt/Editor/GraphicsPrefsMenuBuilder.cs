using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Protobot.UI;

// V2: adds a "Graphics" tab to the Preferences window, made from the window's own pieces (the Game Elements tab
// button, the General page's title and shortcut rows) so it looks like the rest. Run again to rebuild it.
public static class GraphicsPrefsMenuBuilder {
    private const string ScenePath = "Assets/ProtobotRebuilt/Scenes/Main.unity";
    private const string MenuName = "Graphics Prefs Menu";
    private const string TabName = "Graphics Toggle";

    private static readonly (GraphicsPrefsMenu.Option option, string title)[] Rows = {
        (GraphicsPrefsMenu.Option.Quality, "Quality"),
        (GraphicsPrefsMenu.Option.Shadows, "Shadows"),
        (GraphicsPrefsMenu.Option.AntiAliasing, "Anti-Aliasing"),
        (GraphicsPrefsMenu.Option.AmbientOcclusion, "Ambient Occlusion"),
        (GraphicsPrefsMenu.Option.RenderScale, "Render Scale"),
        (GraphicsPrefsMenu.Option.WindowMode, "Window Mode"),
        (GraphicsPrefsMenu.Option.VSync, "V-Sync"),
        (GraphicsPrefsMenu.Option.FrameLimit, "Frame Limit"),
    };

    [MenuItem("Tools/Protobot/Build Graphics Preferences")]
    public static void Build() {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath);

        var canvas = scene.GetRootGameObjects().First(g => g.name == "Preferences Canvas").transform;
        var tabs = canvas.Find("Preferences/Category Selection");
        var pages = canvas.Find("Preferences/Category Container");
        var general = pages.Find("General Prefs Menu");

        // Rebuild from scratch.
        foreach (var old in new[] { tabs.Find(TabName), pages.Find(MenuName) })
            if (old != null)
                Object.DestroyImmediate(old.gameObject);

        // The page: an empty copy of the General page with its title.
        var page = Object.Instantiate(general.gameObject, pages);
        page.name = MenuName;
        foreach (Transform child in page.transform.Cast<Transform>().ToList())
            if (child.name != "Keybinds Title" && child.name != "Shortcut Container")
                Object.DestroyImmediate(child.gameObject);
        var title = page.transform.Find("Keybinds Title");
        title.name = "Graphics Title";
        title.GetComponent<Text>().text = "Graphics";
        var template = page.transform.Find("Shortcut Container").gameObject;
        foreach (var rebind in template.GetComponents<MonoBehaviour>().Where(m => m is not Image && m is not Button).ToList())
            Object.DestroyImmediate(rebind);  // RebindUI: these rows aren't key bindings

        var menu = page.AddComponent<GraphicsPrefsMenu>();
        menu.rows.Clear();
        for (var i = 0; i < Rows.Length; i++) {
            var row = Object.Instantiate(template, page.transform);
            row.name = Rows[i].title + " Setting";
            ((RectTransform)row.transform).anchoredPosition = new Vector2(0f, -40f - 35f * i);
            row.transform.Find("Shortcut Title").GetComponent<Text>().text = Rows[i].title;
            var value = row.transform.Find("Key Mapper");
            var reset = row.transform.Find("Reset Rebinds Button");
            ClearClicks(value.GetComponent<Button>());
            ClearClicks(reset.GetComponent<Button>());
            menu.rows.Add(new GraphicsPrefsMenu.Row {
                option = Rows[i].option,
                valueText = value.GetComponentInChildren<Text>(),
                valueButton = value.GetComponent<Button>(),
                resetButton = reset.GetComponent<Button>(),
            });
        }
        Object.DestroyImmediate(template);
        page.SetActive(false);

        // The tab, under Game Elements, switching to the page like the other tabs do.
        var source = tabs.Find("Game Elements Toggle");
        var tab = Object.Instantiate(source.gameObject, tabs);
        tab.name = TabName;
        var tabRect = (RectTransform)tab.transform;
        tabRect.anchoredPosition = ((RectTransform)source).anchoredPosition + new Vector2(0f, -35f);
        tab.GetComponentInChildren<Text>().text = "Graphics";
        var toggle = tab.GetComponent<Toggle>();
        toggle.SetIsOnWithoutNotify(false);
        while (toggle.onValueChanged.GetPersistentEventCount() > 0)
            UnityEventTools.RemovePersistentListener(toggle.onValueChanged, 0);
        UnityEventTools.AddPersistentListener(toggle.onValueChanged, page.SetActive);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Classic] Built the Graphics page in Preferences.");
    }

    private static void ClearClicks(Button button) {
        while (button.onClick.GetPersistentEventCount() > 0)
            UnityEventTools.RemovePersistentListener(button.onClick, 0);
    }
}
