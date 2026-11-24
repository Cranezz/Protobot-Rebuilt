// (Play) Checks the drive's gear systems: for each motor, its system (rotors, motors, jammed?).
var rotors = Protobot.Motion.GearTrain.Rotors();
var sb = new System.Text.StringBuilder("rotors " + rotors.Count + "\n");
foreach (var m in Protobot.Motion.MotorDrive.All.Where(m => m != null)) {
    var first = rotors.FirstOrDefault(r => r.motors.Contains(m));
    var s = Protobot.Motion.GearTrain.SystemOf(first, rotors);
    sb.AppendLine(m.name + " at " + m.transform.position.ToString("F1") + ": rotors " + s.rotors.Count + " motors " + s.Motors.Count() + " jammed " + s.jammed + " " + s.jamReason);
}
return sb.ToString();
