// Run on: stops the drive, puts a lying pin across the robot 3.5" in front of the intake (from the chassis as it is now),
// intake on, then drives forward slowly. t_intake2 reports where the pin goes.
var g = Protobot.Game.SkillsGame.Current;
var f = g.field;
Rigidbody chassis = null; foreach (var rb in Protobot.Motion.Sandbox.Instance.Root.GetComponentsInChildren<Rigidbody>()) if (rb.gameObject.layer == Protobot.Motion.Sandbox.Layer && (chassis == null || rb.mass > chassis.mass)) chassis = rb;
var front = Vector3.ProjectOnPlane(chassis.transform.forward, Vector3.up).normalized;
var across = Vector3.Cross(Vector3.up, front).normalized;
var at = chassis.transform.TransformPoint(new Vector3(0f, 0f, 13.5f));
at = f.ToWorld(f.ToField(at), 1.55f);
var pin = g.Pins[0];
var rot = Quaternion.FromToRotation(pin.transform.TransformDirection(pin.halves[0].direction), across) * pin.transform.rotation;
pin.body.position = at; pin.body.rotation = rot;
pin.transform.SetPositionAndRotation(at, rot);
pin.body.linearVelocity = Vector3.zero; pin.body.angularVelocity = Vector3.zero;
var sign = -1f;
foreach (var m in UnityEngine.Object.FindObjectsByType<Protobot.Motion.MotorDrive>(UnityEngine.FindObjectsSortMode.None)) {
    var n = Protobot.Motion.Controls.GivenName(m.gameObject) ?? "";
    if (n.StartsWith("Intake")) m.Manual = sign;
}
Protobot.Motion.Sandbox.TestSticks = () => (new Vector2(0f, 0.3f), Vector2.zero);
return "pin at " + f.ToField(at).ToString("F1");
