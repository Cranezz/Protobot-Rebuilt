// Play mode: a 4-post hex nut retainer and a 1-post one: holes, pocket; a hex nut seated in each by
// AxlePlaceDisplacement.Seat (reflection), then a picture (Assets/Screenshots/ret.png).
var sb = new System.Text.StringBuilder();
var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var seat = typeof(Protobot.AxlePlaceDisplacement).GetMethod("Seat", flags);
var nutId = UnityEngine.PlayerPrefs.GetString("t_ret_nut", "NUT-Hex");
var x = 0f;
foreach (var rid in new[] { "VXSTRUCTUREPARTS-4 Post Hex Nut Retainer", "VXSTRUCTUREPARTS-1 Post Hex Nut Retainer", "VXSTRUCTUREPARTS-4 Post Standoff Retainer" }) {
  var ret = Protobot.PartsManager.GeneratePart(rid, new UnityEngine.Vector3(x, 0.5f, 0f), UnityEngine.Quaternion.Euler(-90f, 0f, 0f));
  x += 1.5f;
  var holes = ret.GetComponentsInChildren<Protobot.HoleCollider>(true);
  sb.Append(rid + ": holes " + holes.Length);
  foreach (var h in holes) sb.Append(" [" + h.transform.localScale.x.ToString("0.###") + "x" + h.transform.localScale.y.ToString("0.###") + "]");
  var p = ret.GetComponent<Protobot.Motion.NutPocket>();
  sb.AppendLine(p == null ? " no pocket" : " pocket floor " + p.floor.ToString("0.000") + " out " + p.outward.ToString("0.00") + " depth " + p.depth.ToString("0.000") + " across " + p.across.ToString("0.000"));
  if (p == null) continue;
  var nut = Protobot.PartsManager.GeneratePart(nutId, new UnityEngine.Vector3(0f, 5f, 0f), UnityEngine.Quaternion.identity);
  if (nut == null) { sb.AppendLine("no nut " + nutId); break; }
  Protobot.AxlePlaceDisplacement.Axle axle;
  if (!Protobot.AxlePlaceDisplacement.AxleOf(nut, out axle)) { sb.AppendLine("nut has no axle"); continue; }
  var d = (Protobot.Transformations.Displacement)seat.Invoke(null, new object[] { axle, nut.transform.rotation, nut, p });
  nut.transform.SetPositionAndRotation(d.translation.Position, d.rotation.Orientation);
  sb.AppendLine("  nut at " + nut.transform.position.ToString("0.000") + " pocket floor world " + ret.transform.TransformPoint(p.floor).ToString("0.000"));
}
var cam = UnityEngine.Camera.main;
cam.transform.position = new UnityEngine.Vector3(1.5f, 4f, -3f);
cam.transform.LookAt(new UnityEngine.Vector3(1.5f, 0.5f, 0f));
var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
pt.GetMethod("Frames").Invoke(null, new object[] { 3 });
pt.GetMethod("Screenshot").Invoke(null, new object[] { "ret" });
return sb.ToString();
