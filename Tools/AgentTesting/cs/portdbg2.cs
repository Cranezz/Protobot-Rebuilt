var sb = new System.Text.StringBuilder();
var part = Protobot.PartsManager.GeneratePart("PNMT-50mm", new Vector3(500f, 500f, 500f), Quaternion.identity);
var mesh = part.GetComponent<MeshFilter>().sharedMesh;
var probe = new GameObject("pp"); var collider = probe.AddComponent<MeshCollider>(); collider.sharedMesh = mesh;
var b = mesh.bounds; int ax = 1, sign = -1; int u = 2, v = 0; float Step = 0.01f; int R = 18;
int nu = Mathf.CeilToInt(b.size[u] / Step), nv = Mathf.CeilToInt(b.size[v] / Step);
var dir = Vector3.zero; dir[ax] = -sign;
var depth = new float[nu, nv]; int hits = 0;
for (int i = 0; i < nu; i++) for (int j = 0; j < nv; j++) { var p = Vector3.zero; p[u] = b.min[u] + (i + 0.5f) * Step; p[v] = b.min[v] + (j + 0.5f) * Step; p[ax] = sign > 0 ? b.max[ax] + 0.5f : b.min[ax] - 0.5f;
  depth[i, j] = collider.Raycast(new Ray(p, dir), out var hit, b.size[ax] + 1f) ? hit.distance : 99f; if (depth[i,j] < 90) hits++; }
sb.Append("nu " + nu + " nv " + nv + " hits " + hits + " dir " + dir + "\n");
int cav = 0; var cavity = new bool[nu, nv];
for (int i = R; i < nu - R; i++) for (int j = R; j < nv - R; j++) { var d = depth[i, j]; if (d > 90f) continue; var most = 99f; var whole = true;
  for (int k = 0; k < 24 && whole; k++) { var a = k * Mathf.PI / 12f; var x = i + Mathf.RoundToInt(Mathf.Cos(a) * R); var y = j + Mathf.RoundToInt(Mathf.Sin(a) * R); if (depth[x, y] > 90f) whole = false; most = Mathf.Min(most, depth[x, y]); }
  if (whole && d - most > 0.08f) { cav++; if (cav < 10) sb.Append(" cav at z " + (b.min[u] + i * Step).ToString("F2") + " x " + (b.min[v] + j * Step).ToString("F2") + " d " + d.ToString("F3") + " most " + most.ToString("F3") + "\n"); } }
sb.Append("cavity cells " + cav + "\n");
// depth along x at z = 0
var mid = nu / 2; for (int j = 0; j < nv; j += 10) sb.Append((b.min[v] + j * Step).ToString("F1") + ":" + depth[mid, j].ToString("F2") + " ");
UnityEngine.Object.DestroyImmediate(probe); UnityEngine.Object.DestroyImmediate(part);
return sb.ToString();
