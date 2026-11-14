var sb = new System.Text.StringBuilder();
GameObject gear = null, wheel = null; Protobot.Screw sc = null;
foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None)) {
  if (s.id.StartsWith("GEAR")) gear = s.gameObject; if (s.id.StartsWith("OMNI")) wheel = s.gameObject;
  if (s.id == "SCRW-1.75in") sc = s.GetComponent<Protobot.Screw>();
}
Protobot.HoleCollider mid = null;
foreach (var h in gear.GetComponentsInChildren<Protobot.HoleCollider>()) if (h.transform.parent == gear.transform && (mid == null || (h.transform.position - gear.transform.position).sqrMagnitude < (mid.transform.position - gear.transform.position).sqrMagnitude)) mid = h;
var head = sc.transform.position; var down = -sc.transform.forward;
sb.AppendLine("gear mid hole " + mid.transform.position.ToString("F3") + " fwd " + mid.transform.forward.ToString("F2") + " depth " + mid.transform.lossyScale.z.ToString("F3"));
sb.AppendLine("screw head " + head.ToString("F3") + " down " + down.ToString("F2") + " len " + sc.ScrewLength);
var group = Protobot.ConnectedObjExtensions.GetConnectedObjects(gear, true);
sb.Append("group:"); foreach (var g in group) sb.Append(" " + g.name.Replace("(Clone)","")); sb.AppendLine();
var faceGo = new GameObject("Test Face", typeof(MeshFilter), typeof(MeshRenderer));
var face = faceGo.AddComponent<Protobot.HoleFace>();
var dir = mid.transform.forward * Mathf.Sign(Vector3.Dot(mid.transform.forward, gear.transform.position - wheel.transform.position));
face.hole = mid.holeData; face.direction = dir; faceGo.transform.rotation = Quaternion.LookRotation(-dir, mid.holeData.rotation * Vector3.up); faceGo.transform.position = mid.holeData.position + dir * (mid.holeData.depth / 2f);
var hp = UnityEngine.Object.FindFirstObjectByType<Protobot.HolePlaceDisplacement>();
var mcField = typeof(Protobot.HolePlaceDisplacement).GetField("mouseCast", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var mc = (Protobot.MouseCast)mcField.GetValue(hp);
var t = typeof(Protobot.MouseCast); var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var col = sc.GetComponent<Collider>() ?? sc.GetComponentInChildren<Collider>();
var onto = typeof(Protobot.HolePlaceDisplacement).GetMethod("OntoScrew", bf);
foreach (var m in new[] { 0.15f, 0.6f, 1.0f, 1.5f }) {
  var p = head + down * m;
  var perp = Vector3.Cross(down, Vector3.up).sqrMagnitude > 0.01f ? Vector3.Cross(down, Vector3.up).normalized : Vector3.right;
  RaycastHit hit; var ok = col.Raycast(new Ray(p + perp * 2f, -perp), out hit, 5f);
  t.GetField("cachedFrame", bf).SetValue(mc, Time.frameCount); t.GetField("cachedPosition", bf).SetValue(mc, (Vector2)Protobot.MouseInput.Position);
  t.GetField("cachedMask", bf).SetValue(mc, mc.layerMask); t.GetField("cachedHit", bf).SetValue(mc, hit);
  var args = new object[] { face, sc, null };
  var r = (bool)onto.Invoke(hp, args);
  var d = (Protobot.Transformations.Displacement)args[2];
  if (!r) { sb.AppendLine("mouse " + m + " hit " + ok + " -> refused"); continue; }
  var turn = Quaternion.Angle(d.rotation.Orientation, faceGo.transform.rotation);
  var along = Vector3.Dot(d.translation.Position - head, down);
  sb.AppendLine("mouse " + m + " -> face at along " + along.ToString("F3") + " turn " + turn.ToString("F1") + "° off-axis " + Vector3.ProjectOnPlane(d.translation.Position - head, down).magnitude.ToString("F3"));
}
UnityEngine.Object.Destroy(faceGo);
return sb.ToString();
