var type = Protobot.PartsManager.GetPartType("VXMOTIONPARTS") ?? Protobot.PartsManager.GetPartType("VXWHEELS");
var sb = new System.Text.StringBuilder();
foreach (var fam in new[] { "VXMOTIONPARTS", "VXWHEELS", "VXMISCELLANEOUS" }) {
  var t = Protobot.PartsManager.GetPartType(fam); if (t == null) continue;
  foreach (var d in t.GetComponentsInChildren<Protobot.PartData>(true)) {
    if (!d.param1Value.Contains("Bogie")) continue;
    var mesh = d.GetComponent<UnityEngine.MeshFilter>().sharedMesh; var mats = d.GetComponent<UnityEngine.MeshRenderer>().sharedMaterials;
    sb.Append(fam + " | " + d.param1Value + " | subs " + mesh.subMeshCount + " | ");
    var v = mesh.vertices;
    for (int s = 0; s < mesh.subMeshCount; s++) {
      var tris = mesh.GetTriangles(s);
      // connected pieces by welded vertices
      var weld = new System.Collections.Generic.Dictionary<UnityEngine.Vector3Int, int>(); var pt = new int[v.Length];
      for (int i = 0; i < v.Length; i++) { var k = UnityEngine.Vector3Int.RoundToInt(v[i] * 2000f); int w; if (!weld.TryGetValue(k, out w)) { w = weld.Count; weld[k] = w; } pt[i] = w; }
      var par = new int[weld.Count]; for (int i = 0; i < par.Length; i++) par[i] = i;
      System.Func<int,int> find = null; find = x => { while (par[x] != x) x = par[x] = par[par[x]]; return x; };
      for (int i = 0; i < tris.Length; i += 3) { var a = find(pt[tris[i]]); par[find(pt[tris[i+1]])] = a; par[find(pt[tris[i+2]])] = a; }
      var groups = new System.Collections.Generic.Dictionary<int, UnityEngine.Bounds>(); var counts = new System.Collections.Generic.Dictionary<int,int>();
      for (int i = 0; i < tris.Length; i++) { var r = find(pt[tris[i]]); if (!groups.ContainsKey(r)) { groups[r] = new UnityEngine.Bounds(v[tris[i]], UnityEngine.Vector3.zero); counts[r] = 0; } var b = groups[r]; b.Encapsulate(v[tris[i]]); groups[r] = b; counts[r]++; }
      sb.Append("\n   sub" + s + " " + (s < mats.Length && mats[s] ? mats[s].name : "?") + ": " + groups.Count + " pieces: ");
      foreach (var kv in groups) if (counts[kv.Key] > 60) sb.Append("[c" + kv.Value.center.ToString("F2") + " s" + kv.Value.size.ToString("F2") + " n" + counts[kv.Key]/3 + "] ");
    }
    sb.Append("\n");
  }
}
return sb.ToString();
