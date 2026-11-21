// The holes HoleScanner finds on the AI Vision Sensor mesh (what WirePorts uses to find its smart port).
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProtobotRebuilt/Resources/Part Prefabs/VEX/Sensors.prefab");
var sb = new System.Text.StringBuilder();
foreach (var name in new[] { "AI Vision Sensor", "Vision Sensor", "Distance Sensor" }) {
    var p = prefab.transform.Cast<Transform>().First(c => c.name == name);
    var mesh = p.GetComponent<MeshFilter>().sharedMesh;
    sb.AppendLine(name);
    foreach (var h in Protobot.Geometry.HoleScanner.Scan(mesh.vertices.Select(v => v * 0.0254f).ToList(), mesh.triangles))
        sb.AppendLine("  " + (h.size / 0.0254f).ToString("F3") + " x " + (h.size2 / 0.0254f).ToString("F3") + " square " + h.square + " through " + h.through + " at " + (h.entry / 0.0254f).ToString("F2") + " axis " + h.axis.ToString("F2"));
}
return sb.ToString();
