// The intake on the 6-motor drive: a 12" shaft across the front (upper hole row, z = 6) from a motor bolted outside
// the right outer channel, through both right channels, with four 3" flex wheels between the inner channels.
var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
const float B = 7.911f, Y = 2.125f, Z = 6f;
var motor = Protobot.EditorTools.MotionTestRig.Motor(new Vector3(B + 0.007f, Y, Z), Vector3.left, Vector3.back);
foreach (var z in new[] { Z - 0.5f, Z - 1.0f }) Protobot.EditorTools.MotionTestRig.Screw(new Vector3(B - 0.0555f, Y, z), Vector3.left);
var shaft = Protobot.EditorTools.MotionTestRig.Shaft(new Vector3(2.6f, Y, Z), Vector3.right, 12);
var wheels = new System.Collections.Generic.List<GameObject>();
foreach (var x in new[] { -2.6f, -0.9f, 0.8f, 2.5f }) wheels.Add(Protobot.PartsManager.GeneratePart("FWHL-With Adapters-3.00in", new Vector3(x, Y, Z), Quaternion.LookRotation(Vector3.right)));
pt.GetMethod("Frames").Invoke(null, new object[] { 8 });
var drive = motor.GetComponent<Protobot.Motion.MotorDrive>();
drive.SetCartridge(600, false);
Protobot.Motion.Controls.SetName(motor, "Intake");
Protobot.Motion.Controls.SetForwardKey(motor, UnityEngine.InputSystem.Key.I);
Protobot.Motion.Controls.SetReverseKey(motor, UnityEngine.InputSystem.Key.K);
var rotors = Protobot.Motion.GearTrain.Rotors();
var system = Protobot.Motion.GearTrain.SystemOf(drive, rotors);
var sb = new System.Text.StringBuilder("motor at " + motor.transform.position.ToString("F2") + " shaft hole " + drive.ShaftPoint.ToString("F2") + "\n");
if (system == null) sb.Append("NO SYSTEM for the intake motor\n");
else {
  sb.Append("system rotors " + system.rotors.Count + " jammed " + system.jammed + " " + system.jamReason + "\n");
  foreach (var r in system.rotors) { sb.Append(" rotor parts:"); foreach (var p in r.parts) sb.Append(" " + p.name.Replace("(Clone)", "")); sb.Append("\n"); }
}
return sb.ToString();
