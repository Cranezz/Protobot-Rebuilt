var pl = Protobot.Placement.Instance;
var f = typeof(Protobot.Placement).GetField("currentPlacementData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var data = f.GetValue(pl) as Protobot.GameObjectPlacementData;
var face = data.GetGameObject().GetComponent<Protobot.HoleFace>();
Protobot.Screw sc = null; foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.Screw>(FindObjectsSortMode.None)) if (s.GetComponent<Protobot.SavedObject>().id == "SCRW-1.75in") sc = s;
var hp = UnityEngine.Object.FindFirstObjectByType<Protobot.HolePlaceDisplacement>();
var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var mc = (Protobot.MouseCast)typeof(Protobot.HolePlaceDisplacement).GetField("mouseCast", bf).GetValue(hp);
var t = typeof(Protobot.MouseCast);
var col = sc.GetComponent<Collider>() ?? sc.GetComponentInChildren<Collider>();
var head = sc.transform.position; var down = -sc.transform.forward;
var onto = typeof(Protobot.HolePlaceDisplacement).GetMethod("OntoScrew", bf);
var sb = new System.Text.StringBuilder("grabbed face dir " + face.direction.ToString("F2") + "\n");
foreach (var m in new[] { 0.4f, 1.3f }) {
  var p = head + down * m; var perp = Vector3.Cross(down, Vector3.up).normalized;
  RaycastHit hit; col.Raycast(new Ray(p + perp * 2f, -perp), out hit, 5f);
  t.GetField("cachedFrame", bf).SetValue(mc, Time.frameCount); t.GetField("cachedPosition", bf).SetValue(mc, (Vector2)Protobot.MouseInput.Position);
  t.GetField("cachedMask", bf).SetValue(mc, mc.layerMask); t.GetField("cachedHit", bf).SetValue(mc, hit);
  var args = new object[] { face, sc, null };
  var ok = (bool)onto.Invoke(hp, args);
  var d = (Protobot.Transformations.Displacement)args[2];
  if (!ok) { sb.Append("mouse " + m + ": refused\n"); continue; }
  // Where the grabbed face ends up and which way it points.
  var turn = d.rotation.Orientation * Quaternion.Inverse(face.transform.rotation);
  var facesNow = turn * face.direction;
  sb.Append("mouse " + m + ": grabbed face at " + Vector3.Dot(d.translation.Position - head, down).ToString("F3") + " pointing " + (Vector3.Dot(facesNow, down) < 0 ? "toward the head (-)" : "toward the tip (+)") + "\n");
}
return sb.ToString();
