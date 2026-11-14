// Temp/depth.txt: "ID param1 param2 axis(x|-x|y|-y|z|-z) step": a depth map of the part seen from that side (rays
// into it); prints rows of digits (depth in 0.1" past the nearest surface) and the deep pockets found.
var a = System.IO.File.ReadAllText("Temp/depth.txt").Trim().Split(' ');
var g = Protobot.PartsManager.GetPartType(a[0]).GetComponent<Protobot.PartGenerator>();
if (a[1] != "-") g.param1.value = a[1].Replace('_', ' ');
if (a[2] != "-") g.param2.value = a[2].Replace('_', ' ');
var o = g.Generate(new Vector3(0f, 200f, 0f), Quaternion.identity);
var probe = new GameObject("depth probe"); probe.transform.position = o.transform.position; probe.layer = 2;
var mc = probe.AddComponent<MeshCollider>(); mc.sharedMesh = o.GetComponent<MeshFilter>().sharedMesh;
Physics.SyncTransforms();
var axis = a[3]; var step = float.Parse(a[4]);
Vector3 dir = axis == "x" ? Vector3.right : axis == "-x" ? Vector3.left : axis == "y" ? Vector3.up : axis == "-y" ? Vector3.down : axis == "z" ? Vector3.forward : axis == "-z" ? Vector3.back : Vector3.zero;
if (dir == Vector3.zero) { var c = axis.Split(','); dir = new Vector3(float.Parse(c[0]), float.Parse(c[1]), float.Parse(c[2])).normalized; }
// rays travel along -dir (from the dir side into the part)
var b = mc.bounds;
Vector3 u = Mathf.Abs(dir.y) > 0.5f ? Vector3.right : (Mathf.Abs(dir.x) > 0.5f ? Vector3.forward : Vector3.right);
Vector3 w = Vector3.Cross(dir, u);
u = Vector3.ProjectOnPlane(u, dir).normalized; w = Vector3.Cross(dir, u);
float ue = Vector3.Dot(b.extents, new Vector3(Mathf.Abs(u.x), Mathf.Abs(u.y), Mathf.Abs(u.z)));
float we = Vector3.Dot(b.extents, new Vector3(Mathf.Abs(w.x), Mathf.Abs(w.y), Mathf.Abs(w.z)));
float de = Vector3.Dot(b.extents, new Vector3(Mathf.Abs(dir.x), Mathf.Abs(dir.y), Mathf.Abs(dir.z)));
float wLo = a.Length > 5 ? float.Parse(a[5]) : -we, wHi = a.Length > 6 ? float.Parse(a[6]) : we;
var sb = new System.Text.StringBuilder("u " + u + " w " + w + " (rows: w from + to -, cols: u from - to +)\n");
float face = float.MaxValue;
int nu = Mathf.CeilToInt(2f * ue / step), nw = Mathf.CeilToInt((wHi - wLo) / step);
var depth = new float[nu, nw];
for (int i = 0; i < nu; i++) for (int j = 0; j < nw; j++) {
  var p = b.center + u * (-ue + (i + 0.5f) * step) + w * (wHi - (j + 0.5f) * step) + dir * (de + 1f);
  RaycastHit hit;
  depth[i, j] = mc.Raycast(new Ray(p, -dir), out hit, 2f * de + 2f) ? hit.distance - 1f : 99f;
  if (depth[i, j] < face) face = depth[i, j];
}
// Pockets: cells deeper than 0.8" past the face, grouped along u; each group's middle (where the ray entered and hit).
var cells = new System.Collections.Generic.List<int[]>();
for (int i = 0; i < nu; i++) for (int j = 0; j < nw; j++) if (depth[i, j] - face > 0.8f && depth[i, j] < 50f) cells.Add(new[] { i, j });
var groups = new System.Collections.Generic.List<System.Collections.Generic.List<int[]>>();
foreach (var c in cells) {
  System.Collections.Generic.List<int[]> into = null;
  foreach (var gr in groups) foreach (var e in gr) if (Mathf.Abs(e[0] - c[0]) <= 1 && Mathf.Abs(e[1] - c[1]) <= 2) { into = gr; break; }
  if (into == null) { into = new System.Collections.Generic.List<int[]>(); groups.Add(into); }
  into.Add(c);
}
foreach (var gr in groups) {
  float ci = 0, cj = 0; foreach (var e in gr) { ci += e[0]; cj += e[1]; } ci /= gr.Count; cj /= gr.Count;
  var p0 = b.center + u * (-ue + (ci + 0.5f) * step) + w * (wHi - (cj + 0.5f) * step) + dir * (de + 1f);
  RaycastHit hit2; var bottom = mc.Raycast(new Ray(p0, -dir), out hit2, 2f * de + 2f) ? hit2.point - o.transform.position : Vector3.zero;
  // The rim around the pocket: the shallowest cells a few cells out.
  float rim = 99f;
  for (int di = -6; di <= 6; di++) for (int dj = -6; dj <= 6; dj++) { int ii = (int)ci + di, jj = (int)cj + dj; if (ii >= 0 && jj >= 0 && ii < nu && jj < nw && depth[ii, jj] < rim) rim = depth[ii, jj]; }
  var entry = p0 - dir * (1f + rim) - o.transform.position;
  int i0 = 9999, i1 = -1, j0 = 9999, j1 = -1; foreach (var e in gr) { i0 = Mathf.Min(i0, e[0]); i1 = Mathf.Max(i1, e[0]); j0 = Mathf.Min(j0, e[1]); j1 = Mathf.Max(j1, e[1]); }
  sb.Append("pocket " + gr.Count + " entry " + entry.ToString("F3") + " span u " + ((i1 - i0 + 1) * step).ToString("F2") + " w " + ((j1 - j0 + 1) * step).ToString("F2") + System.Environment.NewLine);
}
if (a.Length > 7) { UnityEngine.Object.Destroy(probe); UnityEngine.Object.Destroy(o); return sb.ToString(); }
for (int j = 0; j < nw; j++) { for (int i = 0; i < nu; i++) { var d = depth[i, j] - face; sb.Append(d > 50f ? "." : d < 0.05f ? " " : ((int)Mathf.Min(9f, d * 10f)).ToString()); } sb.Append("\n"); }
UnityEngine.Object.Destroy(probe); UnityEngine.Object.Destroy(o);
return sb.ToString();
