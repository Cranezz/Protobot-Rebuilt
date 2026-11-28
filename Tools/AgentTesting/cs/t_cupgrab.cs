// (Play, a run going, demo robot) Temp/cupgrab.txt: "lying" or "standing" puts a cup 3.5" in front of the intake
// (lying: across the robot), intake on, drives forward slowly; "where" reports the cup in the chassis frame; "stop".
var cmd = System.IO.File.ReadAllText("Temp/cupgrab.txt").Trim();
var g = Protobot.Game.SkillsGame.Current; var f = g.field;
Rigidbody chassis = null; foreach (var rb in Protobot.Motion.Sandbox.Instance.Root.GetComponentsInChildren<Rigidbody>()) if (rb.gameObject.layer == Protobot.Motion.Sandbox.Layer && (chassis == null || rb.mass > chassis.mass)) chassis = rb;
var cup = g.Cups[0];
if (cmd == "where") {
    var local = chassis.transform.InverseTransformPoint(cup.body.position);
    return "cup height " + f.HeightOf(cup.body.position).ToString("F2") + " chassis frame " + local.ToString("F2") + " speed " + cup.body.linearVelocity.magnitude.ToString("F1") + " tilt " + Vector3.Angle(cup.transform.TransformDirection(cup.opaqueDirection), Vector3.up).ToString("F0");
}
if (cmd == "stop") { Protobot.Motion.Sandbox.TestSticks = () => (Vector2.zero, Vector2.zero); foreach (var m in UnityEngine.Object.FindObjectsByType<Protobot.Motion.MotorDrive>(UnityEngine.FindObjectsSortMode.None)) m.Manual = 0f; return "stopped"; }
var front = Vector3.ProjectOnPlane(chassis.transform.forward, Vector3.up).normalized;
var across = Vector3.Cross(Vector3.up, front).normalized;
var lying = cmd == "lying";
var at = chassis.transform.TransformPoint(new Vector3(0f, 0f, 13.8f));
at = f.ToWorld(f.ToField(at), lying ? 1.65f : 3.25f);
var axis = cup.transform.TransformDirection(cup.opaqueDirection);
var rot = Quaternion.FromToRotation(axis, lying ? across : Vector3.up) * cup.transform.rotation;
cup.body.position = at; cup.body.rotation = rot; cup.transform.SetPositionAndRotation(at, rot);
cup.body.linearVelocity = Vector3.zero; cup.body.angularVelocity = Vector3.zero; cup.body.WakeUp();
foreach (var m in UnityEngine.Object.FindObjectsByType<Protobot.Motion.MotorDrive>(UnityEngine.FindObjectsSortMode.None)) {
    var n = Protobot.Motion.Controls.GivenName(m.gameObject) ?? "";
    if (n.StartsWith("Intake")) m.Manual = -1f;
}
Protobot.Motion.Sandbox.TestSticks = () => (new Vector2(0f, 0.3f), Vector2.zero);
return "cup at " + f.ToField(at).ToString("F1") + " " + cmd;
