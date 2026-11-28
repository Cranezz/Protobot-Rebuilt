// (Play, after t_screwbrng.cs made the rig) The hole MouseCast picks while placing, for every mouse pixel round the
// bearing's mounting hole (the same steps as MouseCast.Cast/NearestHole, then TopOfStack when it exists).
var sb = new System.Text.StringBuilder();
var cam = Camera.main;
Protobot.HoleCollider mount = null; GameObject brng = null;
foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None)) if (s.name.StartsWith("Normal")) brng = s.gameObject;
foreach (var h in brng.GetComponentsInChildren<Protobot.HoleCollider>()) if (Vector3.Dot(h.transform.position - brng.transform.position, brng.transform.right) > 0.4f) mount = h;
var mountTop = mount.transform.position + Vector3.up * mount.transform.lossyScale.z / 2f;
var top = typeof(Protobot.MouseCast).GetMethod("TopOfStack", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
var mask = LayerMask.GetMask("Default", "HoleCollisions");
int bearing = 0, channel = 0, none = 0, fixedBearing = 0, fixedChannel = 0, fixedOther = 0, changed = 0, changedBad = 0;
foreach (var pitch in new[] { 35f, 55f, 75f }) foreach (var yaw in new[] { 0f, 90f, 200f, 300f }) {
  cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
  cam.transform.position = mountTop - cam.transform.forward * 5f;
  var c = cam.WorldToScreenPoint(mountTop);
  for (var dx = -16; dx <= 16; dx += 2) for (var dy = -16; dy <= 16; dy += 2) {
    var mouse = new Vector2(c.x + dx, c.y + dy);
    var ray = cam.ScreenPointToRay(mouse);
    RaycastHit pick = new RaycastHit();
    if (Physics.Raycast(ray, out var hit, Mathf.Infinity, mask)) {
      if (hit.collider.CompareTag("HoleCollider")) pick = hit;
      else {
        var maxD = hit.distance + 0.35f; var bestPx = 14f * Mathf.Max(1f, Screen.dpi / 96f);
        foreach (var cand in Physics.SphereCastAll(ray, 0.5f, Mathf.Infinity, mask)) {
          if (!cand.collider.CompareTag("HoleCollider")) continue;
          var hole = cand.collider.transform; var axis = hole.forward;
          var facing = Vector3.Dot(axis, ray.direction) > 0f ? -axis : axis;
          var nf = hole.position + facing * hole.lossyScale.z / 2f;
          if (Vector3.Distance(ray.origin, nf) > maxD) continue;
          var sp = cam.WorldToScreenPoint(nf); if (sp.z <= 0f) continue;
          var px = Vector2.Distance(mouse, sp); if (px >= bestPx) continue;
          bestPx = px; pick = cand; pick.normal = facing; pick.point = nf;
        }
      }
    }
    System.Func<RaycastHit, int> Who = h => h.collider == null ? 0 : h.collider.transform.root.gameObject == brng ? 1 : 2;
    var who = Who(pick);
    if (who == 0) none++; else if (who == 1) bearing++; else {
      // only the channel's hole under the bearing's counts
      var o = pick.collider.transform.position - mount.transform.position;
      if (new Vector2(o.x, o.z).magnitude < 0.05f && o.y < 0f) channel++; else none++;
    }
    if (top != null && pick.collider != null && !pick.collider.CompareTag("x")) {
      var after = hit.collider != null && hit.collider.CompareTag("HoleCollider") ? pick : (RaycastHit)top.Invoke(null, new object[] { pick, ray, hit.distance + 0.35f });
      var w2 = Who(after);
      if (after.collider != pick.collider) { changed++; if (w2 != 1) changedBad++; }
      if (w2 == 1) fixedBearing++; else if (w2 == 2) { var o = after.collider.transform.position - mount.transform.position; if (new Vector2(o.x, o.z).magnitude < 0.05f && o.y < 0f) fixedChannel++; else fixedOther++; }
    }
  }
}
sb.AppendLine("old pick: bearing's hole " + bearing + ", channel's hole under it " + channel + ", other/none " + none);
if (top != null) sb.AppendLine("with TopOfStack: bearing's hole " + fixedBearing + ", channel's hole under it " + fixedChannel + ", other holes " + fixedOther + "; picks changed " + changed + " (to something other than the bearing: " + changedBad + ")");
return sb.ToString();
