var sb = new System.Text.StringBuilder();
var t = Protobot.PartsManager.GetPartType("VXHARDWARE");
foreach (var pd in t.GetComponentsInChildren<Protobot.PartData>(true)) {
  var n = pd.gameObject.name; if (!(n.Contains("Coupler") || n.Contains("Rubber"))) continue;
  var mesh = pd.GetComponent<MeshFilter>().sharedMesh; var b = mesh.bounds;
  sb.Append(n + " bounds c " + b.center.ToString("F3") + " s " + b.size.ToString("F3") + " holes " + pd.GetComponentsInChildren<Protobot.HoleCollider>(true).Length + " comps " + string.Join(",", System.Linq.Enumerable.Select(pd.GetComponents<Component>(), c => c.GetType().Name)) + "\n");
  // radius profile along z (axis guess: longest)
  var axis = b.size.x >= b.size.y && b.size.x >= b.size.z ? 0 : b.size.y >= b.size.z ? 1 : 2;
  var v = mesh.vertices; int bins = 24; var rmax = new float[bins];
  float lo = b.min[axis], hi = b.max[axis];
  foreach (var p in v) { int k = Mathf.Clamp((int)((p[axis] - lo) / (hi - lo) * bins), 0, bins - 1); var q = p - b.center; q[axis] = 0; rmax[k] = Mathf.Max(rmax[k], q.magnitude); }
  sb.Append("  axis " + axis + " from " + lo.ToString("F3") + " to " + hi.ToString("F3") + ": ");
  for (int k = 0; k < bins; k++) sb.Append(rmax[k].ToString("F3") + " ");
  sb.Append("\n");
}
return sb.ToString();
