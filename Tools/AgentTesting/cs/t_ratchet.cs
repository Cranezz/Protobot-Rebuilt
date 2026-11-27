// Play mode: a ratchet on a shaft along z at (35, 20, 0) and its pawl resting on the teeth, leaning one way then the
// other: the way the shaft can't turn (GearKit.Blocked) should flip; and a turn the blocked way does nothing.
var sb = new System.Text.StringBuilder();
foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None)) if (s.transform.position.x > 33f && s.transform.position.x < 38f) UnityEngine.Object.Destroy(s.gameObject);
var C = new UnityEngine.Vector3(35f, 20f, 0f); var Z = UnityEngine.Vector3.forward;
Protobot.PartsManager.GeneratePart("SHFT-Normal-2", C, UnityEngine.Quaternion.LookRotation(Z));
var r = Protobot.PartsManager.GeneratePart("VXADVANCEDGEARS-Ratchet Gear", C, UnityEngine.Quaternion.identity);
var rs = Protobot.Motion.GearKit.Measure(r); var rrot = UnityEngine.Quaternion.FromToRotation(rs.axis, Z); r.transform.SetPositionAndRotation(C - rrot * rs.center, rrot);
var pawl = Protobot.PartsManager.GeneratePart("VXADVANCEDGEARS-Winch Pawl", C, UnityEngine.Quaternion.identity);
var ps = Protobot.Motion.GearKit.Measure(pawl);
// the pawl's tip in its own space (farthest from its pivot hole)
var verts = pawl.GetComponent<UnityEngine.MeshFilter>().sharedMesh.vertices; var tipLocal = ps.center; var far = 0f;
foreach (var v in verts) { var d = (v - ps.center).sqrMagnitude; if (d > far) { far = d; tipLocal = v; } }
var len = UnityEngine.Mathf.Sqrt(far);
var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
foreach (var lean in new[] { 35f, -35f }) {
  // pivot out along +x past the rim; tip back toward the centre, leaning round by `lean` degrees
  var tipWorld = C + UnityEngine.Vector3.right * (rs.radius - 0.03f);
  var dirTipToPivot = UnityEngine.Quaternion.AngleAxis(lean, Z) * UnityEngine.Vector3.right;
  var pivotWorld = tipWorld + dirTipToPivot * len;
  var localDir = (ps.center - tipLocal).normalized;
  var rot = UnityEngine.Quaternion.FromToRotation(rrot * UnityEngine.Vector3.zero + (UnityEngine.Quaternion.FromToRotation(ps.axis, Z) * localDir), dirTipToPivot) * UnityEngine.Quaternion.FromToRotation(ps.axis, Z);
  pawl.transform.SetPositionAndRotation(pivotWorld - rot * ps.center, rot);
  pt.GetMethod("Frames").Invoke(null, new object[] { 1 });
  var rotors = Protobot.Motion.GearTrain.Rotors();
  Protobot.Motion.Rotor rotor = null; foreach (var x in rotors) if (x.parts.Contains(r)) rotor = x;
  if (rotor == null) { sb.AppendLine("no rotor"); continue; }
  var sys = Protobot.Motion.GearTrain.SystemOf(rotor, rotors);
  string why; var run = Protobot.Motion.MotorRunner.Begin(sys, null, out why);
  var plus = Protobot.Motion.MotorRunner.Apply(run, 10f); var minus = Protobot.Motion.MotorRunner.Apply(run, -10f);
  Protobot.Motion.MotorRunner.End(run);
  sb.AppendLine("lean " + lean + ": blocked " + Protobot.Motion.GearKit.Blocked(rotor) + " (axis " + rotor.axis.ToString("0") + ") turned +10 -> " + plus.ToString("0") + ", -10 -> " + minus.ToString("0") + " | tip at r " + ((pawl.transform.TransformPoint(tipLocal) - C).magnitude).ToString("0.00"));
}
return sb.ToString();
