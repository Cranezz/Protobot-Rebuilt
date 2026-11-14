Protobot.PartsManager.DestroyLoadedObjects();
var ch = Protobot.PartsManager.GeneratePart("CCHL-1x2-5", new UnityEngine.Vector3(0f, 2f, 0f), UnityEngine.Quaternion.identity);
UnityEngine.Physics.SyncTransforms();
var mc = ch.GetComponent<UnityEngine.MeshCollider>();
var probe = new UnityEngine.GameObject("probe"); var pc = probe.AddComponent<UnityEngine.MeshCollider>(); pc.sharedMesh = ch.GetComponent<UnityEngine.MeshFilter>().sharedMesh; probe.transform.SetPositionAndRotation(ch.transform.position, ch.transform.rotation); probe.transform.localScale = ch.transform.lossyScale; probe.layer = 30;
UnityEngine.Physics.SyncTransforms();
var sb = new System.Text.StringBuilder();
var holes = ch.GetComponentsInChildren<Protobot.HoleCollider>();
var picked = new System.Collections.Generic.List<Protobot.HoleCollider>();
// one hole per distinct axis
foreach (var h in holes) { var ax = h.transform.forward; bool seen = false; foreach (var q in picked) if (UnityEngine.Mathf.Abs(UnityEngine.Vector3.Dot(q.transform.forward, ax)) > 0.9f && UnityEngine.Mathf.Abs(UnityEngine.Vector3.Dot(q.transform.position - h.transform.position, ax)) < 0.05f) seen = true; if (!seen) picked.Add(h); if (picked.Count >= 3) break; }
foreach (var h in picked) {
  var axis = h.transform.forward; var origin = h.transform.position - axis * 2f;
  var used = Protobot.AxleLine.Used(null, origin, axis, 4f);
  // the real wall: rays along the axis beside the hole (just outside its edge) hitting the mesh
  var side = UnityEngine.Vector3.Cross(axis, UnityEngine.Mathf.Abs(UnityEngine.Vector3.Dot(axis, UnityEngine.Vector3.up)) > 0.9f ? UnityEngine.Vector3.right : UnityEngine.Vector3.up).normalized;
  var off = side * (h.transform.lossyScale.x / 2f + 0.03f);
  UnityEngine.RaycastHit a, b;
  var hitA = pc.Raycast(new UnityEngine.Ray(origin + off, axis), out a, 4f);
  var hitB = pc.Raycast(new UnityEngine.Ray(origin + off + axis * 4f, -axis), out b, 4f);
  sb.Append("hole axis " + axis.ToString("F1") + ": used ");
  foreach (var u in used) sb.Append("[" + u.from.ToString("F3") + ", " + u.to.ToString("F3") + "] ");
  sb.Append(" | mesh wall " + (hitA ? a.distance.ToString("F3") : "-") + " .. " + (hitB ? (4f - b.distance).ToString("F3") : "-") + "\n");
}
UnityEngine.Object.Destroy(probe);
return sb.ToString();
