var sb = new System.Text.StringBuilder();
var ids = new[] { "PNMT-50mm", "VXPNEUMATICS-Fitting for Cylinders", "VXPNEUMATICS-Fitting for Reservoir", "VXPNEUMATICS-Fitting for Valves", "VXPNEUMATICS-Cylinder Mount", "VXPNEUMATICS-Cylinder Rod Pivot", "VXPNEUMATICS-Piston Nut", "VXPNEUMATICS-V5 Pneumatic Air Reservoir", "VXPNEUMATICS-Legacy Pneumatic Air Reservoir", "VXPNEUMATICS-Solenoid   Double Acting", "VXPNEUMATICS-Manifold Assembly" };
foreach (var id in ids) {
  var part = Protobot.PartsManager.GeneratePart(id, new Vector3(500f, 500f, 500f), Quaternion.identity);
  var mesh = part.GetComponent<MeshFilter>().sharedMesh; var b = mesh.bounds;
  sb.Append(id + ": bounds c " + b.center.ToString("F3") + " s " + b.size.ToString("F3") + " children " + part.transform.childCount + "\n");
  foreach (var h in part.GetComponentsInChildren<Protobot.HoleCollider>(true)) { var t = h.transform; sb.Append("   hole " + part.transform.InverseTransformPoint(t.position).ToString("F3") + " axis " + part.transform.InverseTransformDirection(t.forward).ToString("F2") + " " + t.lossyScale.x.ToString("F3") + "x" + t.lossyScale.y.ToString("F3") + " d " + t.lossyScale.z.ToString("F3") + " " + h.holeType + " parent " + t.parent.name + "\n"); }
  // radius profile along the longest axis
  var axis = b.size.x >= b.size.y && b.size.x >= b.size.z ? 0 : b.size.y >= b.size.z ? 1 : 2;
  if (id.Contains("Fitting") || id.Contains("Nut") || id.Contains("Pivot")) {
    var v = mesh.vertices; int bins = 30; var rmax = new float[bins]; float lo = b.min[axis], hi = b.max[axis];
    foreach (var p in v) { int k = Mathf.Clamp((int)((p[axis] - lo) / (hi - lo) * bins), 0, bins - 1); var q = p - b.center; q[axis] = 0; rmax[k] = Mathf.Max(rmax[k], q.magnitude); }
    sb.Append("   profile axis " + "xyz"[axis] + " " + lo.ToString("F3") + ".." + hi.ToString("F3") + ": "); for (int k = 0; k < bins; k++) sb.Append(rmax[k].ToString("F3") + " "); sb.Append("\n");
  }
  UnityEngine.Object.DestroyImmediate(part);
}
return sb.ToString();
