// One-off (V2): replaces Easy Performant Outline's Outlinable (and its TargetStateListener) in Main.unity with
// Protobot.Outlining.FaceOutline (same enabled state, color and target renderers), then saves the scene.
var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
if (!scene.path.EndsWith("Main.unity")) return "open scene is " + scene.path;
var sb = new System.Text.StringBuilder();
var all = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.MonoBehaviour>();
int moved = 0, removed = 0;
foreach (var m in all) {
    if (m == null || m.gameObject.scene != scene) continue;
    var t = m.GetType();
    if (t.FullName != "EPOOutline.Outlinable") continue;
    var go = m.gameObject;
    var single = t.GetProperty("RenderStyle").GetValue(m).ToString() == "Single";
    var parameters = t.GetProperty(single ? "OutlineParameters" : "FrontParameters").GetValue(m);
    var color = (UnityEngine.Color)parameters.GetType().GetProperty("Color").GetValue(parameters);
    var on = (bool)parameters.GetType().GetProperty("Enabled").GetValue(parameters);
    var renderers = new System.Collections.Generic.List<UnityEngine.Renderer>();
    foreach (var target in (System.Collections.IEnumerable)t.GetProperty("OutlineTargets").GetValue(m)) {
        var r = target.GetType().GetProperty("Renderer").GetValue(target) as UnityEngine.Renderer;
        if (r != null) renderers.Add(r);
    }
    var face = UnityEditor.Undo.AddComponent<Protobot.Outlining.FaceOutline>(go);
    face.color = color;
    face.targets = renderers.ToArray();
    face.enabled = m.enabled && on;
    sb.Append(go.name + ": enabled " + face.enabled + ", color " + color + ", " + renderers.Count + " renderers\n");
    UnityEngine.Object.DestroyImmediate(m, true);
    moved++;
}
foreach (var m in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.MonoBehaviour>()) {
    if (m == null || m.gameObject.scene != scene) continue;
    if (m.GetType().Namespace == "EPOOutline") { sb.Append("removed " + m.GetType().Name + " on " + m.gameObject.name + "\n"); UnityEngine.Object.DestroyImmediate(m, true); removed++; }
}
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
var saved = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
return sb + "moved " + moved + ", removed " + removed + ", saved " + saved;
