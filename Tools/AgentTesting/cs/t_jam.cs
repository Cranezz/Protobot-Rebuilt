var rotors = Protobot.Motion.GearTrain.Rotors();
var sb = new System.Text.StringBuilder();
foreach (var m in UnityEngine.Object.FindObjectsByType<Protobot.Motion.MotorDrive>(UnityEngine.FindObjectsSortMode.None)) {
    var sys = Protobot.Motion.GearTrain.SystemOf(m, rotors);
    sb.Append((Protobot.Motion.Controls.GivenName(m.gameObject) ?? "?") + ": " + (sys == null ? "none" : sys.rotors.Count + " rotors" + (sys.jammed ? " JAMMED " + sys.jamReason : "")) + "; ");
}
return sb.ToString();
