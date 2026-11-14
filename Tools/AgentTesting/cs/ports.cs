var sb = new System.Text.StringBuilder();
foreach (var id in new[] { "PNMT-50mm", "PNMT-25mm", "VXPNEUMATICS-V5 Pneumatic Air Reservoir", "VXPNEUMATICS-Legacy Pneumatic Air Reservoir" }) {
  var part = Protobot.PartsManager.GeneratePart(id, new Vector3(500f, 500f, 500f), Quaternion.identity);
  var mfs = part.GetComponentsInChildren<MeshFilter>();
  sb.Append(id + " renderers: "); foreach (var mf in mfs) sb.Append(mf.name + " " + mf.sharedMesh.bounds.size.ToString("F2") + " @" + part.transform.InverseTransformPoint(mf.transform.position).ToString("F2") + "; "); sb.Append("\n");
  var probes = new System.Collections.Generic.List<Collider>();
  foreach (var mf in mfs) { if (mf.GetComponentInParent<Protobot.HoleCollider>() != null) continue; var g = new GameObject("p"); g.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation); g.transform.localScale = mf.transform.lossyScale; var c = g.AddComponent<MeshCollider>(); c.sharedMesh = mf.sharedMesh; probes.Add(c); }
  var b = new Bounds(part.transform.position, Vector3.zero); foreach (var c in probes) b.Encapsulate(c.bounds);
  for (int ax = 0; ax < 3; ax++) for (int sgn = -1; sgn <= 1; sgn += 2) {
    int u = (ax + 1) % 3, v = (ax + 2) % 3; float step = 0.02f;
    int nu = Mathf.CeilToInt(b.size[u] / step), nv = Mathf.CeilToInt(b.size[v] / step);
    var depth = new float[nu, nv]; var dir = Vector3.zero; dir[ax] = -sgn;
    for (int i = 0; i < nu; i++) for (int j = 0; j < nv; j++) {
      var p = b.min; p[u] += (i + 0.5f) * step; p[v] += (j + 0.5f) * step; p[ax] = sgn > 0 ? b.max[ax] + 0.5f : b.min[ax] - 0.5f;
      float best = 99f; foreach (var c in probes) if (c.Raycast(new Ray(p, dir), out var h, b.size[ax] + 1f)) best = Mathf.Min(best, h.distance); depth[i, j] = best;
    }
    int R = 8; // 0.16" ring
    for (int i = R; i < nu - R; i++) for (int j = R; j < nv - R; j++) {
      var d = depth[i, j]; if (d > 90f) continue; float ringMin = 99f; bool hole = true;
      for (int k = 0; k < 16; k++) { int x = i + Mathf.RoundToInt(Mathf.Cos(k * 0.3927f) * R), y = j + Mathf.RoundToInt(Mathf.Sin(k * 0.3927f) * R); ringMin = Mathf.Min(ringMin, depth[x, y]); if (depth[x, y] > 90f) hole = false; }
      // local maximum of depth only
      if (hole && d - ringMin > 0.1f && d >= depth[i - 1, j] && d >= depth[i + 1, j] && d >= depth[i, j - 1] && d >= depth[i, j + 1] && d > depth[i-1,j] - 0.0001f) {
        var p = b.min; p[u] += (i + 0.5f) * step; p[v] += (j + 0.5f) * step; p[ax] = sgn > 0 ? b.max[ax] + 0.5f - ringMin : b.min[ax] - 0.5f + ringMin;
        // its diameter: open cells (depth > ringMin + 0.05) in a row through it
        int w = 0; for (int x = i; x < nu && depth[x, j] > ringMin + 0.05f; x++) w++; for (int x = i - 1; x >= 0 && depth[x, j] > ringMin + 0.05f; x--) w++;
        sb.Append("  cavity facing " + (sgn > 0 ? "+" : "-") + "xyz"[ax] + " at " + part.transform.InverseTransformPoint(p).ToString("F2") + " deep " + (d - ringMin).ToString("F2") + " across ~" + (w * step).ToString("F2") + "\n");
      }
    }
  }
  foreach (var c in probes) UnityEngine.Object.DestroyImmediate(c.gameObject);
  UnityEngine.Object.DestroyImmediate(part);
}
return sb.ToString();
