// Play mode, after t_kit1: a grounded motor on the rack rig's pinion shaft (x = 10). Step 0: place it and start the
// simulation with the motor running; step 1: read how far the rack's copy moved.
var step = UnityEngine.PlayerPrefs.GetInt("t_rm", 0);
UnityEngine.PlayerPrefs.SetInt("t_rm", step + 1);
Protobot.Motion.MotorDrive motor = null;
foreach (var m in UnityEngine.Object.FindObjectsByType<Protobot.Motion.MotorDrive>(UnityEngine.FindObjectsSortMode.None)) if (m.transform.position.x > 8f && m.transform.position.x < 12f && m.transform.position.y > 18f) motor = m;
if (step == 0) {
  UnityEngine.GameObject rack = null; foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None)) if (s.id == "MCMS-Rack-") rack = s.gameObject;
  UnityEngine.GameObject shaft = null; foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.Shaft>(UnityEngine.FindObjectsSortMode.None)) if (UnityEngine.Mathf.Abs(s.transform.position.x - 10f) < 0.2f) shaft = s.gameObject;
  var at = shaft.transform.position;
  var rig = System.Type.GetType("Protobot.EditorTools.MotionTestRig, Assembly-CSharp-Editor");
  var mo = (UnityEngine.GameObject)rig.GetMethod("Motor").Invoke(null, new object[] { at - UnityEngine.Vector3.forward * 0.45f, UnityEngine.Vector3.forward, UnityEngine.Vector3.right, "11W" });
  Protobot.Motion.Dynamics.SetToken(mo, "ground", "1");
  UnityEngine.PlayerPrefs.SetFloat("t_rm_x", rack.transform.position.x);
  var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
  pt.GetMethod("Frames").Invoke(null, new object[] { 2 });
  var rotors = Protobot.Motion.GearTrain.Rotors();
  Protobot.Motion.Rotor r = null; foreach (var x in rotors) if (x.shaft == shaft) r = x;
  var sys = r != null ? Protobot.Motion.GearTrain.SystemOf(r, rotors) : null;
  Protobot.Motion.Sandbox.Toggle();
  mo.GetComponent<Protobot.Motion.MotorDrive>().Manual = 0.5f;
  return "motors on shaft " + (r != null ? r.motors.Count : -1) + " sliders " + (sys != null ? sys.sliders.Count : -1) + " sim " + Protobot.Motion.Sandbox.Running;
}
UnityEngine.PlayerPrefs.SetInt("t_rm", 0);
var sb = new System.Text.StringBuilder();
var copies = 0;
foreach (var mr in UnityEngine.Object.FindObjectsByType<UnityEngine.MeshRenderer>(UnityEngine.FindObjectsSortMode.None)) {
  var src = Protobot.Motion.Sandbox.SourceOf(mr);
  if (src != null && src.GetComponentInParent<Protobot.SavedObject>() != null && src.GetComponentInParent<Protobot.SavedObject>().id == "MCMS-Rack-") { sb.Append("rack copy at x " + mr.bounds.center.x.ToString("0.000") + " (build " + UnityEngine.PlayerPrefs.GetFloat("t_rm_x").ToString("0.000") + ") "); copies++; }
}
if (motor != null) sb.Append(" motor level " + motor.Level.ToString("0.00"));
Protobot.Motion.Sandbox.Toggle();
return sb.ToString() + " copies " + copies;
