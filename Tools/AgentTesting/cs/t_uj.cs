// Play mode: a universal joint at (30, 20, 0) with a shaft in each yoke; bent 30 degrees (ManualTo by reflection);
// then the drive system from the near shaft and one turn.
var sb = new System.Text.StringBuilder();
foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None)) if (s.transform.position.x > 28f && s.transform.position.x < 32f) UnityEngine.Object.Destroy(s.gameObject);
var at = new UnityEngine.Vector3(30f, 20f, 0f);
var uj = Protobot.PartsManager.GeneratePart("VXADVANCEDGEARS-Universal Joint", at, UnityEngine.Quaternion.identity);
var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
pt.GetMethod("Frames").Invoke(null, new object[] { 2 });
var holes = uj.GetComponentsInChildren<Protobot.HoleCollider>(true);
sb.AppendLine("holes " + holes.Length);
var i = 0;
foreach (var h in holes) {
  var axis = h.transform.forward; var c = h.transform.position;
  // a 2" shaft whose end sits in the hole, reaching out away from the joint's middle
  var outward = UnityEngine.Vector3.Dot(c - uj.GetComponent<UnityEngine.Renderer>().bounds.center, axis) >= 0f ? axis : -axis;
  Protobot.PartsManager.GeneratePart("SHFT-Normal-2", c + outward * 0.85f, UnityEngine.Quaternion.LookRotation(outward));
  sb.AppendLine("hole " + i++ + " at " + c.ToString("0.00") + " axis " + axis.ToString("0.0") + " parent " + h.transform.parent.name);
}
pt.GetMethod("Frames").Invoke(null, new object[] { 2 });
var j = uj.GetComponent<Protobot.Motion.UniversalJoint>();
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var ok = (bool)typeof(Protobot.Motion.UniversalJoint).GetMethod("BeginManual", flags).Invoke(j, null);
typeof(Protobot.Motion.UniversalJoint).GetMethod("ManualTo", flags).Invoke(j, new object[] { 30f, 0f });
typeof(Protobot.Motion.UniversalJoint).GetMethod("EndManual", flags).Invoke(j, null);
sb.AppendLine("bent " + ok + " bend " + j.Bend);
pt.GetMethod("Frames").Invoke(null, new object[] { 2 });
var rotors = Protobot.Motion.GearTrain.Rotors();
Protobot.Motion.Rotor near = null;
foreach (var r in rotors) if (r.parts.Contains(uj)) near = r;
if (near == null) return sb.AppendLine("no rotor holds the joint's near yoke").ToString();
var sys = Protobot.Motion.GearTrain.SystemOf(near, rotors);
sb.Append("system rotors " + sys.rotors.Count + " jammed " + sys.jammed + " " + sys.jamReason);
foreach (var r in sys.rotors) sb.Append(" [" + string.Join(",", System.Linq.Enumerable.Select(r.parts, p => p.name)) + " " + sys.ratio[r].ToString("0.00") + "]");
string why; var run = Protobot.Motion.MotorRunner.Begin(sys, null, out why);
if (run == null) return sb.AppendLine(" can't: " + why).ToString();
var far = j.Yoke.rotation;
Protobot.Motion.MotorRunner.Apply(run, 90f);
Protobot.Motion.MotorRunner.End(run);
sb.AppendLine(" | far yoke turned " + UnityEngine.Quaternion.Angle(far, j.Yoke.rotation).ToString("0.0") + " deg for 90");
return sb.ToString();
