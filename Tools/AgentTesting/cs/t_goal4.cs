// Edit mode: FeaturePicker.FindEdges (the Measure tool's edge finder) on each Override goal's mesh, timed; and how
// MeshSlicer-style (int, int) edge keys spread there.
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/ProtobotRebuilt/Resources/Part Prefabs/1Competition/Override Goal.prefab");
var find = typeof(Protobot.Interface.FeaturePicker).GetMethod("FindEdges", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
var sb = new System.Text.StringBuilder();
sb.AppendLine("EdgeKeys " + (typeof(Protobot.Geometry.EdgeKeys) != null));
foreach (UnityEngine.Transform child in prefab.transform) {
  var m = child.GetComponent<UnityEngine.MeshFilter>().sharedMesh;
  var sw = System.Diagnostics.Stopwatch.StartNew();
  var e = find.Invoke(null, new object[] { m });
  var a = (UnityEngine.Vector3[])e.GetType().GetField("a").GetValue(e);
  var cs = (UnityEngine.Vector3[])e.GetType().GetField("corners").GetValue(e);
  sb.AppendLine(child.name + ": " + m.vertexCount + " verts, " + a.Length + " sharp edges, " + cs.Length + " corners, " + sw.ElapsedMilliseconds + " ms");
}
var red = prefab.transform.Find("Override Goal Red Alliance").GetComponent<UnityEngine.MeshFilter>().sharedMesh;
var tr = red.triangles;
var hashes = new System.Collections.Generic.HashSet<int>();
var n = 0;
for (var t = 0; t + 2 < tr.Length; t += 3) { int x = tr[t], y = tr[t + 1]; hashes.Add((x < y ? (x, y) : (y, x)).GetHashCode()); n++; }
sb.AppendLine("(int, int) keys: " + n + " edges, " + hashes.Count + " distinct hashes");
return sb.ToString();
