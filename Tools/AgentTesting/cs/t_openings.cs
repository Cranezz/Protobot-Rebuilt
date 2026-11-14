var fixes = System.Type.GetType("Protobot.Motion.VexHoleFixes, Assembly-CSharp");
var openings = fixes.GetMethod("Openings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
var sb = new System.Text.StringBuilder();
var type = Protobot.PartsManager.GetPartType("VXSENSORS");
foreach (var data in type.GetComponentsInChildren<Protobot.PartData>(true)) {
  var mesh = data.GetComponent<UnityEngine.MeshFilter>().sharedMesh;
  var probe = new UnityEngine.GameObject("probe"); var col = probe.AddComponent<UnityEngine.MeshCollider>(); col.sharedMesh = mesh;
  var list = (System.Collections.IList)openings.Invoke(null, new object[] { mesh, col });
  sb.Append(data.param1Value + ": ");
  foreach (var o in list) {
    var t = o.GetType();
    float size = (float)t.GetField("size").GetValue(o), size2 = (float)t.GetField("size2").GetValue(o), depth = (float)t.GetField("depth").GetValue(o);
    var c = (UnityEngine.Vector3)t.GetField("centre").GetValue(o); var n = (UnityEngine.Vector3)t.GetField("normal").GetValue(o);
    bool sq = (bool)t.GetField("square").GetValue(o); var outl = t.GetField("outline").GetValue(o);
    sb.Append(size.ToString("F3") + (size2 > size + 0.01f ? "x" + size2.ToString("F3") : "") + (sq ? "sq" : "") + (outl != null ? "(outline)" : "") + " d" + depth.ToString("F2") + " @" + c.ToString("F2") + " n" + n.ToString("F0") + "; ");
  }
  sb.Append("\n");
  UnityEngine.Object.DestroyImmediate(probe);
}
return sb.ToString();
