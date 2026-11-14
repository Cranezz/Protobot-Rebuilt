var ids = System.IO.File.ReadAllLines(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/openings_ids.txt");
var sb = new System.Text.StringBuilder();
foreach (var idRaw in ids) {
  var id = idRaw.Trim(); if (id.Length == 0) continue;
  GameObject part = null; try { part = Protobot.PartsManager.GeneratePart(id, new Vector3(500f, 500f, 500f), Quaternion.identity); } catch (System.Exception e) { sb.Append(id + ": ERROR " + e.Message + "\n"); continue; }
  var mesh = part.GetComponent<MeshFilter>().sharedMesh; var b = mesh.bounds;
  var probe = new GameObject("probe"); probe.hideFlags = HideFlags.HideAndDontSave; var mc = probe.AddComponent<MeshCollider>(); mc.sharedMesh = mesh;
  sb.Append(id + ": bounds c " + b.center.ToString("F3") + " s " + b.size.ToString("F3") + "\n");
  for (int ax = 0; ax < 3; ax++) {
    int u = (ax + 1) % 3, v = (ax + 2) % 3;
    float step = Mathf.Max(0.01f, Mathf.Max(b.size[u], b.size[v]) / 600f);
    int nu = Mathf.CeilToInt(b.size[u] / step), nv = Mathf.CeilToInt(b.size[v] / step);
    if (nu < 3 || nv < 3) continue;
    var open = new bool[nu, nv];
    var dir = Vector3.zero; dir[ax] = 1f;
    for (int i = 0; i < nu; i++) for (int j = 0; j < nv; j++) {
      var p = b.min; p[u] += (i + 0.5f) * step; p[v] += (j + 0.5f) * step; p[ax] = b.min[ax] - 1f;
      open[i, j] = !mc.Raycast(new Ray(p, dir), out _, b.size[ax] + 2f) && !mc.Raycast(new Ray(p + dir * (b.size[ax] + 2f), -dir), out _, b.size[ax] + 2f);
    }
    var seen = new bool[nu, nv]; var q = new System.Collections.Generic.Queue<(int, int)>();
    for (int i = 0; i < nu; i++) for (int j = 0; j < nv; j++) {
      if (!open[i, j] || seen[i, j]) continue;
      int cnt = 0, mini = i, maxi = i, minj = j, maxj = j; bool border = false; float su = 0, sv = 0;
      q.Enqueue((i, j)); seen[i, j] = true;
      while (q.Count > 0) { var item = q.Dequeue(); int a = item.Item1, c = item.Item2; cnt++; su += a; sv += c; mini = Mathf.Min(mini, a); maxi = Mathf.Max(maxi, a); minj = Mathf.Min(minj, c); maxj = Mathf.Max(maxj, c);
        if (a == 0 || c == 0 || a == nu - 1 || c == nv - 1) border = true;
        for (int k = 0; k < 4; k++) { int x = a + (k == 0 ? 1 : k == 1 ? -1 : 0), y = c + (k == 2 ? 1 : k == 3 ? -1 : 0); if (x >= 0 && y >= 0 && x < nu && y < nv && open[x, y] && !seen[x, y]) { seen[x, y] = true; q.Enqueue((x, y)); } } }
      if (border || cnt < 20) continue;
      var cen = b.min; cen[ax] = b.center[ax]; cen[u] += (su / cnt + 0.5f) * step; cen[v] += (sv / cnt + 0.5f) * step;
      float w = (maxi - mini + 1) * step, h = (maxj - minj + 1) * step;
      sb.Append("   axis " + "xyz"[ax] + " centre " + cen.ToString("F3") + " box " + "xyz"[u] + w.ToString("F3") + " x " + "xyz"[v] + h.ToString("F3") + " fill " + (cnt * step * step / (w * h)).ToString("F2") + "\n");
    }
  }
  UnityEngine.Object.DestroyImmediate(probe); UnityEngine.Object.DestroyImmediate(part);
}
System.IO.File.WriteAllText(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/openings.txt", sb.ToString());
return "ok";
