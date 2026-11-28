// (Play) slide seats: clears the build, puts a rail (RAIL-Rail-10) with a classic inner and outer trunk, a VEX rail
// stock (RAILS-2x25) with both, and an outer slide member with an acetal truck, all along x at z 0/3/6/9, then says for
// each truck its member (Check) and how far its mesh goes into its member's (overlap depth by ComputePenetration on
// the meshes' colliders, convex off: an approximation by sampled points).
var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
if (Protobot.Motion.Sandbox.Running) Protobot.Motion.Sandbox.Stop();
Protobot.PartsManager.DestroyLoadedObjects();
var sb = new System.Text.StringBuilder();
var pairs = new System.Collections.Generic.List<(GameObject member, GameObject truck)>();
System.Action<string, float, string[]> Row = (memberId, z, trucks) => {
  var m = Protobot.PartsManager.GeneratePart(memberId, new Vector3(0, 3, z), Quaternion.identity);
  Physics.SyncTransforms();
  var x = -1.5f;
  foreach (var tid in trucks) {
    var seat = Protobot.Motion.SlideTruck.Seat(m, m.transform.TransformPoint(new Vector3(x, 0, 0)), true, tid, null);
    var t = Protobot.PartsManager.GeneratePart(tid, seat.translation.Position, seat.rotation.Orientation);
    pairs.Add((m, t));
    x += 3f;
  }
};
Row("RAIL-Rail-10", 0f, new[] { "LMTK-Inner Slide Trunk", "LMTK-Outer Slide Trunk" });
Row("RAILS-2x25", 3f, new[] { "LMTK-Inner Slide Trunk", "LMTK-Outer Slide Trunk" });
Row("VXSLIDEASSEMBLYP-Outer Slide Member", 6f, new[] { "VXSLIDEASSEMBLYP-Inner Acetal Slide Truck" });
Row("VXSLIDEASSEMBLYP-Inner Slide Member", 9f, new[] { "VXSLIDEASSEMBLYP-Inner Acetal Slide Truck" });
Physics.SyncTransforms();
var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
pt.GetMethod("Frames").Invoke(null, new object[] { 3 });
foreach (var pr in pairs) { var m = pr.member; var t = pr.truck;
  var st = t.GetComponent<Protobot.Motion.SlideTruck>();
  if (st != null) st.Check();
  // how deep the truck's vertices go inside the member's mesh: rays from each truck vertex (a sample) outward... use
  // a closest-point test against a non-convex mesh collider: count vertices inside (a ray up from it crosses the mesh an odd number of times)
  var mc = m.GetComponent<MeshCollider>(); var temp = mc == null;
  if (temp) { mc = m.AddComponent<MeshCollider>(); mc.sharedMesh = m.GetComponent<MeshFilter>().sharedMesh; }
  Physics.SyncTransforms();
  var verts = t.GetComponent<MeshFilter>().sharedMesh.vertices; var inside = 0; var n = 0;
  for (var i = 0; i < verts.Length; i += Mathf.Max(1, verts.Length / 2000)) {
    var p = t.transform.TransformPoint(verts[i]); n++;
    var crossings = 0; var start = p + Vector3.down * 3f; var guard = 0;
    while (guard++ < 40 && mc.Raycast(new Ray(start, Vector3.up), out var h, Vector3.Distance(start, p))) { crossings++; start = h.point + Vector3.up * 0.0005f; }
    var start2 = p + Vector3.up * 3f; var c2 = 0; guard = 0;
    while (guard++ < 40 && mc.Raycast(new Ray(start2, Vector3.down), out var h2, Vector3.Distance(start2, p))) { c2++; start2 = h2.point + Vector3.down * 0.0005f; }
    if (crossings > 0 && c2 > 0 && (crossings % 2 == 1)) inside++;
  }
  if (temp) UnityEngine.Object.Destroy(mc);
  sb.AppendLine(t.name + " on " + m.name + ": linked " + (st != null && st.Member == m) + ", truck vertices inside the member " + inside + " of " + n + ", truck at " + m.transform.InverseTransformPoint(t.transform.position).ToString("F3"));
}
return sb.ToString();
