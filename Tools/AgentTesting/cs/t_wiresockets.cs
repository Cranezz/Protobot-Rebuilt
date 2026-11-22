// (Play) Every smart-port device: the sockets WirePorts finds on its model, and the blind square holes HoleScanner sees
// that could be the socket (size a x b in inches), to see why a device has none.
var sb = new System.Text.StringBuilder();
var ids = new System.Collections.Generic.List<string>();
foreach (var g in Resources.LoadAll<Protobot.PartGenerator>("Part Prefabs")) {
    if (g is Protobot.ChildPartGenerator c) { foreach (Transform child in c.transform) ids.Add(g.GetComponent<Protobot.PartType>() != null ? g.GetComponent<Protobot.PartType>().id + "-" + child.name : child.name); }
}
ids.AddRange(new[] { "MOTR", "RDIO" });
foreach (var t in Resources.LoadAll<Protobot.PartType>("Part Prefabs")) if (t.id.StartsWith("SNSR")) ids.Add(t.id);
var filter = System.IO.File.Exists("Temp/ws.txt") ? System.IO.File.ReadAllText("Temp/ws.txt").Trim() : "";
foreach (var id in ids.Distinct()) {
    if (filter.Length > 0 && !id.Contains(filter)) continue;
    if (!id.StartsWith("VXSENSORS") && !id.StartsWith("MOTR") && !id.StartsWith("RDIO") && !id.StartsWith("SNSR") && !id.StartsWith("VXELECTRONICS")) continue;
    GameObject p = null;
    try { p = Protobot.PartsManager.GeneratePart(id, new Vector3(0f, 5000f, 0f), Quaternion.identity); } catch (System.Exception e) { sb.AppendLine(id + ": " + e.Message); continue; }
    if (p == null) continue;
    var kind = Protobot.Motion.WirePorts.DeviceKind(p);
    if (kind == null) { UnityEngine.Object.Destroy(p); continue; }
    var ports = Protobot.Motion.WirePorts.Of(p);
    sb.Append(id + " [" + kind + "] sockets " + ports.Count);
    if (kind == Protobot.Motion.WireKind.Smart && p.TryGetComponent(out MeshFilter f) && f.sharedMesh != null && f.sharedMesh.isReadable && (ports.Count != 1 || filter.Length > 0)) {
        var verts = f.sharedMesh.vertices.Select(v => v * 0.0254f).ToList();
        foreach (var h in Protobot.Geometry.HoleScanner.Scan(verts, f.sharedMesh.triangles)) {
            var a = h.size / 0.0254f; var b = h.size2 / 0.0254f;
            if (Mathf.Max(a, b) < 0.25f || Mathf.Max(a, b) > 0.8f) continue;
            sb.Append(" | " + (h.through ? "thru " : "blind ") + (h.square ? "sq " : "rd ") + a.ToString("F3") + "x" + b.ToString("F3") + " at " + (h.entry / 0.0254f).ToString("F2"));
        }
    }
    sb.AppendLine();
    UnityEngine.Object.Destroy(p);
}
return sb.ToString();
