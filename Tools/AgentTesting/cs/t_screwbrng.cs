// (Play) A bearing flat on a C-channel, then a screw placed with the real Placement over its mounting hole from a few
// camera angles and mouse spots: which hole the screw goes into and where its head sits against the bearing's top.
var sb = new System.Text.StringBuilder();
foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None)) UnityEngine.Object.Destroy(s.gameObject);
var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
System.Action<int> F = n => pt.GetMethod("Frames").Invoke(null, new object[] { n });
F(2);
var ch = Protobot.PartsManager.GeneratePart("CCHL-1x2-5", new Vector3(0f, 0.5f, 0f), Quaternion.identity);
F(2);
Protobot.HoleCollider hole = null; var best = 1e9f;
var holes = ch.GetComponentsInChildren<Protobot.HoleCollider>();
foreach (var h in holes) { if (Vector3.Dot(h.transform.forward, Vector3.up) < 0.9f && Vector3.Dot(h.transform.forward, Vector3.up) > -0.9f) continue; var d = (h.transform.position - ch.GetComponent<Renderer>().bounds.center).sqrMagnitude; if (d < best) { best = d; hole = h; } }
var top = hole.transform.position + Vector3.up * hole.transform.lossyScale.z / 2f;
Vector3 along = Vector3.zero;
foreach (var h in holes) { var off = h.transform.position - hole.transform.position; if (Mathf.Abs(off.y) < 0.01f && Mathf.Abs(off.magnitude - 0.5f) < 0.01f) { along = off.normalized; break; } }
sb.AppendLine("channel hole top y " + top.y.ToString("F3") + " along " + along.ToString("F2"));
var brng = Protobot.PartsManager.GeneratePart("BRNG-Normal", top - Vector3.up * 0.009f, Quaternion.LookRotation(Vector3.up, Vector3.Cross(Vector3.up, along)));
F(3);
// the bearing's mounting hole at +x
Protobot.HoleCollider mount = null;
foreach (var h in brng.GetComponentsInChildren<Protobot.HoleCollider>()) if (Vector3.Dot(h.transform.position - brng.transform.position, brng.transform.right) > 0.4f) mount = h;
var mountTop = mount.transform.position + Vector3.up * mount.transform.lossyScale.z / 2f;
sb.AppendLine("bearing top y " + (brng.GetComponent<Renderer>().bounds.max.y).ToString("F3") + " mount hole top " + mountTop.ToString("F3"));
var gen = Protobot.PartsManager.GetPartType("SCRW").GetComponent<Protobot.PartGenerator>();
gen.param1.value = gen.GetParam1Options()[2];
var placement = Protobot.Placement.Instance;
var wrong = 0; var total = 0; var okCount = 0; var sunk = 0; var other = 0;
var offs = new System.Collections.Generic.List<Vector3> { Vector3.zero };
foreach (var pitch in new[] { 55f, 75f }) foreach (var yaw in new[] { 0f, 90f, 200f }) foreach (var off in offs) {
  Protobot.PivotCamera.Main.SetTransform(mountTop, new Vector3(pitch, yaw, 0f), -5f);
  if (off == offs[0]) F(4);
  placement.StartPlacing(new Protobot.PartPlacementData(gen, Protobot.PartsManager.GetPartType("SCRW"), placement.transform));
  pt.GetMethod("PointAt", new[] { typeof(Vector3) }).Invoke(null, new object[] { mountTop + off });
  F(2);
  var cd = (Protobot.Transformations.Displacement)typeof(Protobot.Placement).GetField("currentDisplacement", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(placement);
  var p = cd.translation.Position;
  var mcast = Camera.main.GetComponent<Protobot.MouseCast>();
  var spd = UnityEngine.Object.FindAnyObjectByType<Protobot.ScrewPlaceDisplacement>();
  var chf = typeof(Protobot.ScrewPlaceDisplacement).GetField("currentHoleFace", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(spd) as Protobot.HoleFace;
  var faceInfo = chf == null ? "no face" : "face active " + chf.gameObject.activeInHierarchy + " at " + chf.position.ToString("F3") + " dir " + chf.direction.ToString("F2") + " of " + (chf.hole != null && chf.hole.part != null ? chf.hole.part.name : "?") + " src " + (chf.hole != null && chf.hole.source != null ? chf.hole.source.transform.position.ToString("F3") : "?");
  var hitName = faceInfo + " | ray hit " + (mcast != null && mcast.hit.collider != null ? mcast.hit.collider.name + " of " + mcast.hit.collider.transform.root.name : "nothing");
  total++;
  var onBearing = Mathf.Abs(p.y - mountTop.y) < 0.01f;
  var onChannelHole = Mathf.Abs(p.y - top.y) < 0.01f;
  var onAxis = new Vector2(p.x - mountTop.x, p.z - mountTop.z).magnitude < 0.05f;
  if (onAxis && onBearing) okCount++; else if (onAxis && onChannelHole) sunk++; else other++;
  if (onAxis && onChannelHole && wrong++ < 6) { sb.AppendLine("pitch " + pitch + " yaw " + yaw + " off " + off.ToString("F2") + ": screw at " + p.ToString("F3") + (onChannelHole ? " = the CHANNEL's face (sunk " + (mountTop.y - p.y).ToString("F3") + "\")" : "") + " hit " + hitName + " cam " + Camera.main.transform.position.ToString("F2") + " point " + (mcast.hit.collider != null ? mcast.hit.point.ToString("F3") : "")); }
  placement.SendMessage("StopPlacing", SendMessageOptions.DontRequireReceiver);
}
sb.AppendLine(total + " tries: in the bearing's hole " + okCount + ", sunk to the channel's face " + sunk + ", elsewhere (mouse not on a hole) " + other);
return sb.ToString();
