Protobot.Devices.V5Brain.Refresh(true);
var sb = new System.Text.StringBuilder();
foreach (var d in Protobot.Devices.V5Brain.Devices) {
  sb.Append(d.PortLabel).Append(" ").Append(d.kind).Append(" ").Append(d.Name);
  if (d.part != null) sb.Append(" side=").Append(Protobot.Motion.Controls.Side(d.part)).Append(" rev=").Append(Protobot.Motion.Controls.Reversed(d.part));
  var m = d as Protobot.Devices.MotorDevice; if (m != null && m.motor != null) sb.Append(" rpm=").Append(m.motor.Rpm);
  sb.Append("\n");
}
return sb.ToString();
