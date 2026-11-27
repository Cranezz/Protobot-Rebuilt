// Play mode: for each gear-kit part type: its middle hole axis, outer radius about it, extent along it, teeth (peaks
// of the outline round the axis at the widest slice), and for screws the thread lead (peaks of radius along the axis).
var sb = new System.Text.StringBuilder();
var ids = new[] { "MCMS-Bevel Gear-16T", "MCMS-Bevel Gear-32T", "VXADVANCEDGEARS-12T Bevel Gear", "VXADVANCEDGEARS-24T Bevel Gear", "VXADVANCEDGEARS-36T Bevel Gear",
  "MCMS-Worm and Wheel-Gear", "MCMS-Worm and Wheel-Worm", "VXADVANCEDGEARS-Worm Gear", "VXADVANCEDGEARS-36T Worm Wheel",
  "MCMS-Screw Kit-Worm Gear", "MCMS-Screw Kit-Worm Nut", "MCMS-Screw Kit-Bracket", "VXADVANCEDGEARS-Lead Screw Segment", "VXADVANCEDGEARS-Lead Screw Nut",
  "MCMS-Rack-", "VXADVANCEDGEARS-Rack Gear", "MCMS-Drop Off Cam-", "VXADVANCEDGEARS-Drop Off Cam", "MCMS-Cam Follower-", "VXADVANCEDGEARS-Cam Follower",
  "VXADVANCEDGEARS-Cam Follower Mounting Bracket", "MCMS-Hand Crank-", "VXADVANCEDGEARS-Ratchet Gear", "VXADVANCEDGEARS-Winch Pawl", "VXADVANCEDGEARS-Winch Drum" };
foreach (var id in ids) {
  UnityEngine.GameObject p = null;
  try { p = Protobot.PartsManager.GeneratePart(id, new UnityEngine.Vector3(0f, -100f, 0f), UnityEngine.Quaternion.identity); } catch (System.Exception e) { sb.AppendLine(id + ": " + e.Message); continue; }
  if (p == null) { sb.AppendLine(id + ": none"); continue; }
  var mesh = p.GetComponent<UnityEngine.MeshFilter>().sharedMesh;
  var b = mesh.bounds;
  var holes = p.GetComponentsInChildren<Protobot.HoleCollider>();
  Protobot.HoleCollider mid = null; var best = 1e9f;
  foreach (var h in holes) { if (h.transform.parent != p.transform) continue; var d = (h.transform.localPosition - b.center).sqrMagnitude; if (d < best) { best = d; mid = h; } }
  sb.Append(id + ": size " + b.size.ToString("0.000") + " center " + b.center.ToString("0.000") + " holes " + holes.Length);
  if (mid != null) {
    var c = mid.transform.localPosition; var ax = (mid.transform.localRotation * UnityEngine.Vector3.forward).normalized;
    var verts = mesh.vertices; float rMax = 0f, aMin = 1e9f, aMax = -1e9f;
    foreach (var v in verts) { var d = v - c; var a = UnityEngine.Vector3.Dot(d, ax); var r = (d - ax * a).magnitude; rMax = UnityEngine.Mathf.Max(rMax, r); aMin = UnityEngine.Mathf.Min(aMin, a); aMax = UnityEngine.Mathf.Max(aMax, a); }
    // teeth: angular bins of max radius near rMax
    var u = UnityEngine.Vector3.Cross(ax, UnityEngine.Mathf.Abs(ax.y) < 0.9f ? UnityEngine.Vector3.up : UnityEngine.Vector3.right).normalized; var w = UnityEngine.Vector3.Cross(ax, u);
    var bins = new float[720];
    foreach (var v in verts) { var d = v - c; var a = UnityEngine.Vector3.Dot(d, ax); var f = d - ax * a; var r = f.magnitude; var ang = UnityEngine.Mathf.Atan2(UnityEngine.Vector3.Dot(f, w), UnityEngine.Vector3.Dot(f, u)) * UnityEngine.Mathf.Rad2Deg; var i = ((int)UnityEngine.Mathf.Floor((ang + 360f) * 2f)) % 720; bins[i] = UnityEngine.Mathf.Max(bins[i], r); }
    var peaks = 0; var thr = rMax * 0.97f; var above = bins[719] > thr;
    for (var i = 0; i < 720; i++) { var now = bins[i] > thr; if (now && !above) peaks++; above = now; }
    // lead: radius along the axis, peaks of the max radius per 0.01" slice
    var slices = UnityEngine.Mathf.Max(1, (int)((aMax - aMin) / 0.01f)); var prof = new float[slices + 1];
    foreach (var v in verts) { var d = v - c; var a = UnityEngine.Vector3.Dot(d, ax); var r = (d - ax * a).magnitude; var i = UnityEngine.Mathf.Clamp((int)((a - aMin) / 0.01f), 0, slices); prof[i] = UnityEngine.Mathf.Max(prof[i], r); }
    sb.Append(" | hole " + mid.transform.localScale.x.ToString("0.000") + " " + mid.holeType + " axis " + ax.ToString("0.0") + " rOuter " + rMax.ToString("0.000") + " along " + aMin.ToString("0.000") + ".." + aMax.ToString("0.000") + " teeth~" + peaks);
  }
  sb.AppendLine();
  UnityEngine.Object.Destroy(p);
}
return sb.ToString();
