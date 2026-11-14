var sb = new System.Text.StringBuilder();
foreach (var t in Resources.LoadAll<Protobot.PartType>("Part Prefabs")) {
  foreach (var pd in t.GetComponentsInChildren<Protobot.PartData>(true)) {
    if (!pd.gameObject.name.Contains("Bogie")) continue;
    var mesh = pd.GetComponent<MeshFilter>().sharedMesh;
    sb.Append(t.name + " / " + pd.gameObject.name + " id " + t.id + " p1 " + pd.param1Value + " sub " + mesh.subMeshCount + " verts " + mesh.vertexCount + " bounds " + mesh.bounds.center.ToString("F2") + " " + mesh.bounds.size.ToString("F2") + "\n");
    var mats = pd.GetComponent<MeshRenderer>().sharedMaterials;
    var verts = mesh.vertices;
    for (int s = 0; s < mesh.subMeshCount; s++) {
      var tris = mesh.GetTriangles(s); var b = new Bounds(verts[tris[0]], Vector3.zero); foreach (var i in tris) b.Encapsulate(verts[i]);
      sb.Append("  sub " + s + " " + (s < mats.Length && mats[s] != null ? mats[s].name : "-") + " tris " + tris.Length/3 + " " + b.center.ToString("F2") + " " + b.size.ToString("F2") + "\n");
    }
    // connected components by welded position
    var all = mesh.triangles; var key = new System.Collections.Generic.Dictionary<Vector3Int,int>(); var id = new int[verts.Length];
    for (int i = 0; i < verts.Length; i++) { var k = Vector3Int.RoundToInt(verts[i] * 2000f); if (!key.TryGetValue(k, out var v)) { v = key.Count; key[k] = v; } id[i] = v; }
    var parent = new int[key.Count]; for (int i = 0; i < parent.Length; i++) parent[i] = i;
    System.Func<int,int> find = null; find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
    for (int i = 0; i < all.Length; i += 3) { var a = find(id[all[i]]); var b2 = find(id[all[i+1]]); var c = find(id[all[i+2]]); parent[b2] = a; parent[find(c)] = a; }
    var comp = new System.Collections.Generic.Dictionary<int, Bounds>(); var cnt = new System.Collections.Generic.Dictionary<int,int>();
    for (int i = 0; i < all.Length; i++) { var r = find(id[all[i]]); if (comp.TryGetValue(r, out var bb)) { bb.Encapsulate(verts[all[i]]); comp[r] = bb; cnt[r]++; } else { comp[r] = new Bounds(verts[all[i]], Vector3.zero); cnt[r] = 1; } }
    foreach (var kv in comp) if (cnt[kv.Key] > 300) sb.Append("  comp tris " + cnt[kv.Key]/3 + " " + kv.Value.center.ToString("F2") + " " + kv.Value.size.ToString("F2") + "\n");
    sb.Append("  comps total " + comp.Count + "\n");
  }
}
return sb.ToString();
