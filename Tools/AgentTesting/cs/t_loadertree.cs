// Edit mode: the Loader's parts in the field prefabs (names, bounds in field inches) -> Temp/loadertree.txt
var sb = new System.Text.StringBuilder();
foreach (var path in new[] { "Assets/Protobot/Field/Override/Override Field (light).prefab", "Assets/Protobot/Field/Override/Override Field.prefab" }) {
    var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
    if (prefab == null) { sb.AppendLine("missing " + path); continue; }
    sb.AppendLine("== " + path);
    var root = prefab.transform;
    Transform first = null;
    foreach (var t in prefab.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("276-9250-830")) { first = t; break; }
    if (first == null) { foreach (var t in prefab.GetComponentsInChildren<Transform>(true)) if (t.name.Contains("Loader")) { sb.AppendLine("loader-ish: " + t.name + " parent " + (t.parent ? t.parent.name : "")); } continue; }
    sb.AppendLine("assembly: " + first.name + " parent " + first.parent.name);
    foreach (var t in first.GetComponentsInChildren<Transform>(true)) {
        var depth = 0; for (var p = t; p != first; p = p.parent) depth++;
        var r = t.GetComponent<MeshRenderer>(); var f = t.GetComponent<MeshFilter>();
        var b = r != null ? r.bounds : default(Bounds);
        sb.AppendLine(new string(' ', depth * 2) + t.name + (r != null ? "  [" + (f.sharedMesh ? f.sharedMesh.vertexCount : 0) + " v] min " + b.min.ToString("F2") + " max " + b.max.ToString("F2") + " mat " + (r.sharedMaterial ? r.sharedMaterial.name : "") : ""));
    }
}
System.IO.File.WriteAllText("Temp/loadertree.txt", sb.ToString());
return "ok";
