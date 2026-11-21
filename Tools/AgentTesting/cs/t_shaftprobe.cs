// Each shaft sensor: has it found its shaft, its position, and Dynamics.Version now.
var sb = new System.Text.StringBuilder("version " + Protobot.Motion.Dynamics.Version + " t " + (Protobot.RobotCode.RobotProgram.Current != null ? Protobot.RobotCode.RobotProgram.Current.Time.ToString("0.00") : "-"));
var shaftField = typeof(Protobot.Devices.ShaftSensor).GetField("shaft", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
foreach (var d in Protobot.Devices.V5Brain.Devices.OfType<Protobot.Devices.ShaftSensor>()) {
    var sh = (GameObject)shaftField.GetValue(d);
    sb.Append(" | " + Protobot.Motion.Controls.GivenName(d.part) + " has " + d.HasShaft + " shaft " + (sh == null ? "null" : sh.name) + " pos " + d.Position.ToString("0"));
}
return sb.ToString();
