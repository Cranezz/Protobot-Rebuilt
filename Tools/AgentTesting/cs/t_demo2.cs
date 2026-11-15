var s = Protobot.EditorTools.MotionTestRig.FinishDemoRobot();
var rotors = Protobot.Motion.GearTrain.Rotors();
var sb = new System.Text.StringBuilder(s);
foreach (var m in UnityEngine.Object.FindObjectsByType<Protobot.Motion.MotorDrive>(UnityEngine.FindObjectsSortMode.None)) {
    if (m.transform.position.y < 3f) continue;
    var sys = Protobot.Motion.GearTrain.SystemOf(m, rotors);
    sb.Append(Protobot.Motion.Controls.GivenName(m.gameObject) + ": ");
    if (sys == null) { sb.AppendLine("NO SYSTEM"); continue; }
    sb.Append("rotors " + sys.rotors.Count + (sys.jammed ? " JAMMED " + sys.jamReason : "") + " |");
    foreach (var r in sys.rotors) { sb.Append(" ["); foreach (var p in r.parts) sb.Append(p.name.Replace("(Clone)", "").Split(' ')[0] + " "); sb.Append("]"); }
    sb.AppendLine();
}
return sb.ToString();
