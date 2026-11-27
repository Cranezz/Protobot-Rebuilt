// Play mode: places washers over a C-channel hole and onto a screw through it with the real Placement (mouse pointed
// at the spot, frames stepped), and reports where each ghost sits relative to the hole / screw.
var sb = new System.Text.StringBuilder();
foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None)) UnityEngine.Object.Destroy(s.gameObject);
var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
System.Action<int> F = n => pt.GetMethod("Frames").Invoke(null, new object[] { n });
F(2);
var ch = Protobot.PartsManager.GeneratePart("CCHL-1x2-5", new UnityEngine.Vector3(0f, 0.5f, 0f), UnityEngine.Quaternion.identity);
F(2);
// the top-facing hole nearest the channel's middle
Protobot.HoleCollider hole = null; var best = 1e9f;
foreach (var h in ch.GetComponentsInChildren<Protobot.HoleCollider>()) { if (UnityEngine.Mathf.Abs(UnityEngine.Vector3.Dot(h.transform.forward, UnityEngine.Vector3.up)) < 0.9f) continue; var d = (h.transform.position - ch.GetComponent<UnityEngine.Renderer>().bounds.center).sqrMagnitude; if (d < best) { best = d; hole = h; } }
var top = hole.transform.position + UnityEngine.Vector3.up * (hole.transform.lossyScale.z / 2f) * UnityEngine.Mathf.Sign(UnityEngine.Vector3.Dot(hole.transform.forward, UnityEngine.Vector3.up));
sb.AppendLine("hole at " + hole.transform.position.ToString("0.000") + " top face y " + top.y.ToString("0.000"));
var onScrew = UnityEngine.PlayerPrefs.GetInt("t_wash_screw", 0) == 1;
UnityEngine.GameObject screw = null;
if (onScrew) {
  var rig = System.Type.GetType("Protobot.EditorTools.MotionTestRig, Assembly-CSharp-Editor");
  var bottom = top - UnityEngine.Vector3.up * hole.transform.lossyScale.z;
  screw = (UnityEngine.GameObject)rig.GetMethod("Screw").Invoke(null, new object[] { bottom, UnityEngine.Vector3.up, "1.00in" });
  F(3);
  sb.AppendLine("screw head at " + screw.transform.position.ToString("0.000") + " fwd " + screw.transform.forward.ToString("0.0"));
  top = top + UnityEngine.Vector3.up * 0.4f;
}
Protobot.PivotCamera.Main.SetTransform(top, new UnityEngine.Vector3(20f, 30f, 0f), -6f);
F(2);
var vx = Protobot.PartsManager.GetPartType("VXSPACERS"); var gen = vx.GetComponent<Protobot.PartGenerator>();
var placement = Protobot.Placement.Instance;
foreach (var name in new[] { "1/4\" Nylon Spacer (5/16\" OD)", "0.02\" Steel Washer (3/8\" OD)", "0.03\" Steel Washer (3/8\" OD)", "1/16\" Steel Washer (3/8\" OD)", "0.03\" Teflon Washer (3/8\" OD)", "0.04\" Teflon Washer (3/8\" OD)" }) {
  gen.param1.value = name;
  placement.StartPlacing(new Protobot.PartPlacementData(gen, vx, placement.transform));
  pt.GetMethod("PointAt", new[] { typeof(UnityEngine.Vector3) }).Invoke(null, new object[] { onScrew ? top : top + new UnityEngine.Vector3(0.05f, 0f, 0.03f) });
  F(3);
  var g = placement.transform;
  var b = g.GetComponentInChildren<UnityEngine.MeshFilter>() != null ? g.GetComponentInChildren<UnityEngine.Renderer>().bounds : new UnityEngine.Bounds();
  var mc = typeof(Protobot.AxlePlaceDisplacement).GetField("mouseCast", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(UnityEngine.Object.FindAnyObjectByType<Protobot.AxlePlaceDisplacement>()) as Protobot.MouseCast;
  var axd = UnityEngine.Object.FindAnyObjectByType<Protobot.AxlePlaceDisplacement>();
  var hf = typeof(Protobot.AxlePlaceDisplacement).GetField("hoverHoleFace", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(axd) as Protobot.HoleFace;
  sb.Append("[face " + (hf == null ? "null" : "active " + hf.gameObject.activeInHierarchy + " dir " + hf.direction.ToString("0.0") + " at " + hf.position.ToString("0.000") + " hole " + (hf.hole != null && hf.hole.part != null ? hf.hole.part.name : "none")) + "] ");
  Protobot.Transformations.Displacement dd; var got = axd.TryGetDisplacement((Protobot.PlacementData)typeof(Protobot.Placement).GetField("currentPlacementData", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(placement), out dd);
  sb.Append("[axle snap " + got + (got ? " -> " + dd.translation.Position.ToString("0.000") + " " + dd.rotation.Orientation.eulerAngles.ToString("0") : "") + "] ");
  sb.Append("[hover " + (mc != null && mc.gameObject != null ? mc.gameObject.name + " layer " + mc.gameObject.layer : "none") + "] ");
  var cd = (Protobot.Transformations.Displacement)typeof(Protobot.Placement).GetField("currentDisplacement", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(placement);
  sb.Append("[target " + cd.translation.Position.ToString("0.000") + " " + cd.rotation.Orientation.eulerAngles.ToString("0") + "] ");
  var ghostAt = g.position; var ghostTurn = g.rotation;
  var before = new System.Collections.Generic.HashSet<Protobot.SavedObject>(UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None));
  placement.PlaceNow();
  F(3);
  Protobot.SavedObject made = null; foreach (var so in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None)) if (!before.Contains(so)) made = so;
  if (made != null) { var mb = made.GetComponent<UnityEngine.Renderer>().bounds; sb.Append("placed at " + made.transform.position.ToString("0.000") + " (ghost " + ghostAt.ToString("0.000") + ", " + (made.transform.position - ghostAt).magnitude.ToString("0.000") + " apart, turn " + UnityEngine.Quaternion.Angle(made.transform.rotation, ghostTurn).ToString("0") + " deg) box y " + mb.min.y.ToString("0.000") + ".." + mb.max.y.ToString("0.000") + " | "); UnityEngine.Object.Destroy(made.gameObject); F(1); }
  else sb.Append("nothing placed | ");
  sb.AppendLine(name + ": ghost at " + g.position.ToString("0.000") + " euler " + g.eulerAngles.ToString("0") + " | its box y " + b.min.y.ToString("0.000") + ".." + b.max.y.ToString("0.000") + " centre " + b.center.ToString("0.000"));
}
placement.SendMessage("StopPlacing", UnityEngine.SendMessageOptions.DontRequireReceiver);
return sb.ToString();
