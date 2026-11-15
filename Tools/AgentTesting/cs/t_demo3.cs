// The low intake motor's settings and system (FinishDemoRobot's filter missed it the first time).
var rotors = Protobot.Motion.GearTrain.Rotors();
var sb = new System.Text.StringBuilder();
foreach (var m in UnityEngine.Object.FindObjectsByType<Protobot.Motion.MotorDrive>(UnityEngine.FindObjectsSortMode.None)) {
    var p = m.transform.position;
    if (!(p.z > 6.5f && p.y < 6f)) continue;
    m.SetCartridge(600, false);
    Protobot.Motion.Controls.SetName(m.gameObject, "Intake low");
    Protobot.Motion.Controls.SetForwardKey(m.gameObject, UnityEngine.InputSystem.Key.I);
    Protobot.Motion.Controls.SetReverseKey(m.gameObject, UnityEngine.InputSystem.Key.K);
    var sys = Protobot.Motion.GearTrain.SystemOf(m, rotors);
    sb.Append("Intake low at " + p.ToString("F1") + ": ");
    if (sys == null) { sb.AppendLine("NO SYSTEM"); continue; }
    sb.Append("rotors " + sys.rotors.Count + (sys.jammed ? " JAMMED " + sys.jamReason : "") + " |");
    foreach (var r in sys.rotors) { sb.Append(" ["); foreach (var q in r.parts) sb.Append(q.name.Replace("(Clone)", "").Split(' ')[0] + " "); sb.Append("]"); }
}
var c = new Vector3(0f, 5f, 3f);
Protobot.PivotCamera.Main.SetTransform(c, new Vector3(25f, 225f, 0f), -40f);
return sb.ToString();
