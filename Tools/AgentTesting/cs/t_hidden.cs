// Share of each mesh's triangles that can't be seen from outside (26 directions): internal detail.
var want = new[] { "MOTR-11W", "SCRW-2.50in", "CCHL-1x2-25", "NUT-Keps", "GEAR-High Strength-36T" };
var sb = new System.Text.StringBuilder();
var done = new System.Collections.Generic.HashSet<UnityEngine.Mesh>();
var dirs = new System.Collections.Generic.List<UnityEngine.Vector3>();
for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++) if (x != 0 || y != 0 || z != 0) dirs.Add(new UnityEngine.Vector3(x, y, z).normalized);
foreach (var so in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None)) {
  if (System.Array.IndexOf(want, so.id) < 0) continue;
  foreach (var f in so.GetComponentsInChildren<UnityEngine.MeshFilter>()) {
    var m = f.sharedMesh; if (m == null || !m.isReadable || done.Contains(m) || f.GetComponent<UnityEngine.MeshRenderer>() == null) continue;
    if (f.gameObject.layer == 11) continue;
    done.Add(m);
    var go = new UnityEngine.GameObject("probe"); go.transform.position = new UnityEngine.Vector3(5000, 5000, 5000);
    var mc = go.AddComponent<UnityEngine.MeshCollider>(); mc.sharedMesh = m;
    UnityEngine.Physics.SyncTransforms();
    var v = m.vertices; var t = m.triangles; int nt = t.Length / 3; int hidden = 0;
    var size = m.bounds.size.magnitude + 1f;
    for (int i = 0; i < nt; i++) {
      var c = (v[t[3*i]] + v[t[3*i+1]] + v[t[3*i+2]]) / 3f + go.transform.position;
      bool seen = false;
      foreach (var d in dirs) {
        UnityEngine.RaycastHit hit;
        if (mc.Raycast(new UnityEngine.Ray(c + d * size, -d), out hit, size * 2f) && (hit.triangleIndex == i || (hit.point - c).sqrMagnitude < 1e-6f)) { seen = true; break; }
      }
      if (!seen) hidden++;
    }
    UnityEngine.Object.DestroyImmediate(go);
    sb.Append(so.id + " / " + f.name + ": " + nt + " tris, hidden from outside " + hidden + " (" + (100.0 * hidden / nt).ToString("0") + "%)\n");
  }
}
return sb.ToString();
