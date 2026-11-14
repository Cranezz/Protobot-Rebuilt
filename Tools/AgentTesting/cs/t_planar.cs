// For each distinct mesh drawn (robot / field): triangles, and how many vertices sit inside a flat area or on a straight
// edge between two flat areas (removable without changing the shape: each saves 2 triangles).
var fieldObj = UnityEngine.GameObject.Find("Game Field");
var seen = new System.Collections.Generic.Dictionary<UnityEngine.Mesh, int>();
var group = new System.Collections.Generic.Dictionary<UnityEngine.Mesh, string>();
foreach (var r in UnityEngine.Object.FindObjectsByType<UnityEngine.MeshRenderer>(UnityEngine.FindObjectsSortMode.None)) {
  if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
  var f = r.GetComponent<UnityEngine.MeshFilter>(); if (f == null || f.sharedMesh == null || !f.sharedMesh.isReadable) continue;
  string g = fieldObj != null && r.transform.IsChildOf(fieldObj.transform) ? "field" : (r.GetComponentInParent<Protobot.SavedObject>() != null ? "robot" : "");
  if (g == "") continue;
  int n; seen.TryGetValue(f.sharedMesh, out n); seen[f.sharedMesh] = n + 1; group[f.sharedMesh] = g;
}
long[] tris = new long[2], drawn = new long[2], removable = new long[2], removableDrawn = new long[2];
foreach (var kv in seen) {
  var m = kv.Key; int gi = group[m] == "robot" ? 0 : 1;
  var v = m.vertices; var idx = m.triangles;
  // weld
  var map = new System.Collections.Generic.Dictionary<UnityEngine.Vector3Int, int>(); var weld = new int[v.Length];
  for (int i = 0; i < v.Length; i++) { var k = new UnityEngine.Vector3Int(UnityEngine.Mathf.RoundToInt(v[i].x * 20000f), UnityEngine.Mathf.RoundToInt(v[i].y * 20000f), UnityEngine.Mathf.RoundToInt(v[i].z * 20000f)); int w; if (!map.TryGetValue(k, out w)) { w = map.Count; map[k] = w; } weld[i] = w; }
  int nv = map.Count; int nt = idx.Length / 3;
  var normals = new UnityEngine.Vector3[nt]; var vertTris = new System.Collections.Generic.List<int>[nv];
  var edgeCount = new System.Collections.Generic.Dictionary<long, int>();
  for (int t = 0; t < nt; t++) {
    int a = weld[idx[3*t]], b = weld[idx[3*t+1]], c = weld[idx[3*t+2]];
    normals[t] = UnityEngine.Vector3.Cross(v[idx[3*t+1]] - v[idx[3*t]], v[idx[3*t+2]] - v[idx[3*t]]).normalized;
    foreach (var x in new[]{a,b,c}) { if (vertTris[x] == null) vertTris[x] = new System.Collections.Generic.List<int>(); vertTris[x].Add(t); }
    foreach (var e in new[]{ new[]{a,b}, new[]{b,c}, new[]{c,a} }) { long key = (long)System.Math.Min(e[0],e[1]) * 4000000L + System.Math.Max(e[0],e[1]); int ec; edgeCount.TryGetValue(key, out ec); edgeCount[key] = ec + 1; }
  }
  long rem = 0;
  for (int x = 0; x < nv; x++) {
    var lt = vertTris[x]; if (lt == null || lt.Count < 3) continue;
    // closed fan? every edge from x shared by exactly 2 triangles
    bool closed = true;
    foreach (var t in lt) { for (int j = 0; j < 3; j++) { int o = weld[idx[3*t+j]]; if (o == x) continue; long key = (long)System.Math.Min(x,o) * 4000000L + System.Math.Max(x,o); if (edgeCount[key] != 2) closed = false; } }
    if (!closed) continue;
    // normal groups
    var groups = new System.Collections.Generic.List<UnityEngine.Vector3>();
    foreach (var t in lt) { bool found = false; foreach (var gn in groups) if (UnityEngine.Vector3.Dot(gn, normals[t]) > 0.99999f) { found = true; break; } if (!found) groups.Add(normals[t]); if (groups.Count > 2) break; }
    if (groups.Count == 1) { rem++; continue; }
    if (groups.Count == 2) {
      // straight crease: the two neighbours on the crease line up through x
      var crease = new System.Collections.Generic.List<UnityEngine.Vector3>();
      foreach (var t in lt) for (int j = 0; j < 3; j++) { int o = weld[idx[3*t+j]]; if (o == x) continue;
        // edge x-o is on the crease when the two triangles sharing it are in different groups: approximate by the neighbour being in both groups' triangles
        int inA = 0, inB = 0; foreach (var t2 in lt) { bool has = weld[idx[3*t2]] == o || weld[idx[3*t2+1]] == o || weld[idx[3*t2+2]] == o; if (!has) continue; if (UnityEngine.Vector3.Dot(groups[0], normals[t2]) > 0.99999f) inA++; else inB++; }
        if (inA > 0 && inB > 0) crease.Add(v[idx[3*t+j]]); }
      // find the welded position of x
      UnityEngine.Vector3 px = UnityEngine.Vector3.zero; for (int t0 = 0; t0 < 3; t0++) if (weld[idx[3*lt[0]+t0]] == x) px = v[idx[3*lt[0]+t0]];
      var uniq = new System.Collections.Generic.List<UnityEngine.Vector3>(); foreach (var p in crease) { bool dup = false; foreach (var q in uniq) if ((p-q).sqrMagnitude < 1e-8f) dup = true; if (!dup) uniq.Add(p); }
      if (uniq.Count == 2 && UnityEngine.Vector3.Dot((uniq[0]-px).normalized, (uniq[1]-px).normalized) < -0.99999f) rem++;
    }
  }
  tris[gi] += nt; drawn[gi] += (long)nt * kv.Value; removable[gi] += rem * 2; removableDrawn[gi] += rem * 2 * kv.Value;
}
return "robot: " + seen.Count + " meshes; stored tris " + tris[0] + ", removable " + removable[0] + " (" + (100.0 * removable[0] / System.Math.Max(1, tris[0])).ToString("0.0") + "%), drawn " + drawn[0] + " removable drawn " + removableDrawn[0]
 + " | field: stored " + tris[1] + ", removable " + removable[1] + " (" + (100.0 * removable[1] / System.Math.Max(1, tris[1])).ToString("0.0") + "%), drawn " + drawn[1] + " removable drawn " + removableDrawn[1] + " (" + (100.0 * removableDrawn[1] / System.Math.Max(1, drawn[1])).ToString("0.0") + "%)";
