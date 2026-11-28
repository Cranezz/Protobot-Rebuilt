// (Play) String phase 2 (physics). Temp/str3.txt: "rig" (StringRig at the origin, after a clear), "finish MODE SLACK"
// (FinishStringRig), "sim" (starts Simulate), "drive POWER" (the arm's motor, -1..1, by MotorDrive.Manual), "read"
// (strings in the sim: slack, pull, peak, broken; the arm's turn; the pull the motor's stall torque gives at the
// string's lever), "weak LBF" (VEX rope's rating for this test; "weak" alone puts 100 back), "stop", "step N" (N
// stepped frames, then read). Phases 3-4: "finish winch SLACK" (post A wound 3 turns on the motor's shaft collar),
// "finish pin SLACK" (a loose 1" standoff on the plate tied to post A), "lines [N]" (after N frames: each string's turns,
// free length, tight length: winding in the build), "lift N" (the pin pulled up at 10 in/s for N frames: does it let go).
// Run from str.sh (s3 "command").
var cmd = System.IO.File.ReadAllText("Temp/str3.txt").Trim();
try {
var rig = System.Type.GetType("Protobot.EditorTools.MotionTestRig, Assembly-CSharp-Editor");
var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
var parts = (System.Collections.Generic.Dictionary<string, GameObject>)rig.GetField("StringParts").GetValue(null);
if (cmd == "rig") {
  rig.GetMethod("Clear").Invoke(null, null);
  pt.GetMethod("Frames").Invoke(null, new object[] { 2 });
  var made = (System.Collections.Generic.List<GameObject>)rig.GetMethod("StringRig").Invoke(null, new object[] { Vector3.zero });
  for (var i = 0; i < 4; i++) pt.GetMethod("Frames").Invoke(null, new object[] { 5 });
  var arm = parts["arm"]; var post = parts["arm post"];
  return "made " + made.Count;
}
if (cmd.StartsWith("finish")) {
  var f = cmd.Split(' ');
  return (string)rig.GetMethod("FinishStringRig").Invoke(null, new object[] { f[1], float.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture) });
}
if (cmd == "sim") { Protobot.Motion.Sandbox.Begin(); return "sim " + Protobot.Motion.Sandbox.Running; }
if (cmd == "stop") { Protobot.Motion.Sandbox.Stop(); return "stopped"; }
if (cmd.StartsWith("weak")) {
  var k = Protobot.Motion.StringKind.Of("VEX");
  k.strength = cmd.Length > 5 ? float.Parse(cmd.Substring(5), System.Globalization.CultureInfo.InvariantCulture) : 100f;
  return "VEX rope rated " + k.strength;
}
if (cmd.StartsWith("drive")) {
  var power = float.Parse(cmd.Substring(6), System.Globalization.CultureInfo.InvariantCulture);
  parts["motor"].GetComponent<Protobot.Motion.MotorDrive>().Manual = power;
  return "manual " + power;
}
if (cmd.StartsWith("step")) {
  var n = int.Parse(cmd.Substring(5));
  for (var i = 0; i < n; i += 10) pt.GetMethod("Frames").Invoke(null, new object[] { 10 });
  cmd = "read";
}
if (cmd.StartsWith("lift")) {
  // lift N: the pin's body pulled straight up at 10 in/s for N stepped frames (as a piston would)
  var n = int.Parse(cmd.Substring(5));
  var rb = Protobot.Motion.Sandbox.BodyOf(parts["pin"]);
  var log = "pin body " + (rb ? rb.name + " mass " + rb.mass.ToString("F3") : "none");
  for (var i = 0; i < n; i++) { if (rb) { rb.linearVelocity = Vector3.up * 10f; rb.angularVelocity = Vector3.zero; } pt.GetMethod("Frames").Invoke(null, new object[] { 1 }); }
  foreach (var l in Protobot.Motion.StringLine.All) { var pr = Protobot.Motion.Sandbox.Pins(l); log += " | pins " + pr.pins + " released " + pr.released + " line.released " + l.released; }
  return log + " pin y " + (rb ? rb.position.y.ToString("F2") : "-");
}
if (cmd.StartsWith("lines")) {
  if (cmd.Length > 6) { var n = int.Parse(cmd.Substring(6)); for (var i = 0; i < n; i += 10) pt.GetMethod("Frames").Invoke(null, new object[] { 10 }); }
  var sb2 = new System.Text.StringBuilder();
  var md2 = parts["motor"].GetComponent<Protobot.Motion.MotorDrive>();
  sb2.AppendLine("sim " + Protobot.Motion.Sandbox.Running + " motor level " + md2.Level.ToString("F2") + " arm y-angle " + parts["arm"].transform.eulerAngles.y.ToString("F1"));
  foreach (var l in Protobot.Motion.StringLine.All) sb2.AppendLine("line turns " + l.turns.ToString("F3") + " length " + l.length.ToString("F3") + " tight " + (l.Path != null ? l.Path.tight.ToString("F3") : "-") + " | " + l.Describe().Replace((char)10, ' '));
  return sb2.ToString();
}
if (cmd == "read") {
  var sb = new System.Text.StringBuilder();
  var arm = parts["arm"];
  var dir = Protobot.Motion.Sandbox.SimDirection(arm, arm.transform.right);
  var turned = Vector3.SignedAngle(arm.transform.right, dir, Vector3.up);
  sb.AppendLine("arm turned " + turned.ToString("F1") + " deg (y)  frame " + Time.frameCount + " t " + Time.time.ToString("F2") + " paused " + UnityEditor.EditorApplication.isPaused);
  var bArm = Protobot.Motion.Sandbox.BodyOf(arm); var bPost = Protobot.Motion.Sandbox.BodyOf(parts["arm post"]); var bShaft = Protobot.Motion.Sandbox.BodyOf(parts["shaft"]); var bPlate = Protobot.Motion.Sandbox.BodyOf(parts["plate"]);
  sb.AppendLine("bodies: arm " + (bArm ? bArm.name : "-") + " post " + (bPost ? bPost.name : "-") + " shaft " + (bShaft ? bShaft.name : "-") + " plate " + (bPlate ? bPlate.name : "-") + " A " + (Protobot.Motion.Sandbox.BodyOf(parts["A"]) ? Protobot.Motion.Sandbox.BodyOf(parts["A"]).name : "-"));
  if (bArm && bPlate) {
    var rel = (Quaternion.Inverse(bPlate.rotation) * bArm.rotation).eulerAngles.y;
    var postAt = Protobot.Motion.Sandbox.SimPoint(parts["arm post"], parts["arm post"].transform.position);
    var aAt = Protobot.Motion.Sandbox.SimPoint(parts["A"], parts["A"].transform.position);
    var shaftAt = Protobot.Motion.Sandbox.SimPoint(parts["shaft"], parts["shaft"].transform.position);
    var toA = Vector3.ProjectOnPlane(aAt - postAt, Vector3.up); var armV = Vector3.ProjectOnPlane(postAt - shaftAt, Vector3.up);
    sb.AppendLine("arm vs plate " + rel.ToString("F1") + " deg; post to A " + toA.magnitude.ToString("F3") + "\"; arm radius " + armV.magnitude.ToString("F2") + "\", angle arm/string " + Vector3.Angle(armV, toA).ToString("F1") + " deg; plate w " + (bPlate.angularVelocity * Mathf.Rad2Deg).ToString("F1") + " v " + bPlate.linearVelocity.ToString("F2") + " arm w rel " + ((bArm.angularVelocity - bPlate.angularVelocity).y * Mathf.Rad2Deg).ToString("F1"));
  }
  sb.AppendLine("ids: arm " + (bArm ? bArm.GetInstanceID() : 0) + " post " + (bPost ? bPost.GetInstanceID() : 0) + " shaft " + (bShaft ? bShaft.GetInstanceID() : 0) + " plate " + (bPlate ? bPlate.GetInstanceID() : 0) + " arm mass " + (bArm ? bArm.mass.ToString("F3") : "-"));
  foreach (var j in UnityEngine.Object.FindObjectsByType<ConfigurableJoint>(FindObjectsSortMode.None)) {
    if (Mathf.Abs(j.linearLimit.limit - 40f) > 0.01f) continue;
    var aw = j.transform.TransformPoint(j.anchor); var cw = j.connectedBody ? j.connectedBody.transform.TransformPoint(j.connectedAnchor) : j.connectedAnchor;
    sb.AppendLine("string joint on " + j.GetComponent<Rigidbody>().GetInstanceID() + " to " + (j.connectedBody ? j.connectedBody.GetInstanceID() : 0) + " force " + j.currentForce.magnitude.ToString("F1") + " (" + (j.currentForce.magnitude / (4.448f * 39.37f)).ToString("F2") + " lbf) torque " + j.currentTorque.magnitude.ToString("F1") + " dist " + Vector3.Distance(aw, cw).ToString("F3") + " anchor " + aw.ToString("F2"));
  }
  var md = parts["motor"].GetComponent<Protobot.Motion.MotorDrive>();
  sb.AppendLine("motor level " + md.Level.ToString("F2") + " arm spin " + (bArm ? (bArm.angularVelocity.y * Mathf.Rad2Deg).ToString("F1") : "-") + " deg/s, plate spin " + (bPlate ? (bPlate.angularVelocity.y * Mathf.Rad2Deg).ToString("F1") : "-") + " deg/s at " + (bPlate ? bPlate.position.ToString("F2") : "-"));
  var shaftPoint = Protobot.Motion.Sandbox.SimPoint(parts["shaft"], parts["shaft"].transform.position);
  foreach (var s in Protobot.Motion.Sandbox.Strings()) {
    sb.AppendLine("wound " + Protobot.Motion.Sandbox.Wound(s.line).ToString("F3") + "\" turns now " + s.line.TurnsNow.ToString("F2"));
    sb.AppendLine("string slack " + s.slack.ToString("F3") + " pull " + s.tension.ToString("F2") + " lbf, peak " + s.peak.ToString("F2") + (s.broken ? " BROKEN" : "") + " moving " + s.moving);
    // The pull the motor's stall torque (2.1 NÂ.m at 100 rpm) makes at this string: torque / lever of its strands on the arm's post.
    var line = s.line;
    var path = line.Path;
    if (path == null || !path.Valid) continue;
    var armPost = parts["arm post"];
    float lever = 0f; int strands = 0;
    foreach (var sp in path.spans) {
      // a span with one end on the arm's post (the end near it), pulling toward the other
      for (var e = 0; e < 2; e++) {
        var on = e == 0 ? sp.a : sp.b; var other = e == 0 ? sp.b : sp.a;
        var local = Protobot.Motion.Sandbox.SimPoint(armPost, armPost.transform.position);
        if (Vector3.ProjectOnPlane(on - local, Vector3.up).magnitude > 0.4f) continue;
        var u = (other - on).normalized;
        lever += Vector3.Dot(Vector3.Cross(on - shaftPoint, u), Vector3.up);
        strands++;
      }
    }
    var stallLbf = 2.1f / Mathf.Max(1e-4f, Mathf.Abs(lever) * 0.0254f) / 4.448f;
    // What Turn applies to a stopped train each step: a I / (I + b dt) (its implicit motor step), a = stall x command.
    var inertiaM = typeof(Protobot.Motion.Sandbox).GetMethod("Inertia", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    var I = (float)inertiaM.Invoke(null, new object[] { bArm, Vector3.up, shaftPoint });
    var stallSim = 2.1f * 1550.0031f; var free = 100f * 2f * Mathf.PI / 60f; var bb = stallSim / free;
    var applied = stallSim * Mathf.Abs(md.Manual) * I / (I + bb * Time.fixedDeltaTime) / 1550.0031f;
    var appliedLbf = applied / Mathf.Max(1e-4f, Mathf.Abs(lever) * 0.0254f) / 4.448f;
    sb.AppendLine("  strands on the arm " + strands + ", lever sum " + lever.ToString("F3") + "\" -> stall pull " + stallLbf.ToString("F2") + " lbf; the sandbox's motor applies " + applied.ToString("F3") + " N.m (I " + I.ToString("F4") + ") -> " + appliedLbf.ToString("F2") + " lbf");
  }
  return sb.ToString();
}
return "?";
} catch (System.Exception ex) { var e = ex; while (e.InnerException != null) e = e.InnerException; return "ERROR " + e.GetType().Name + ": " + e.Message + " " + e.StackTrace; }
