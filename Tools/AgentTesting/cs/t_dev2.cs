var sb = new System.Text.StringBuilder();
foreach (var d in Protobot.Devices.V5Brain.Devices) {
    sb.Append(d.PortLabel + " " + d.Name + ": " + d.Reading);
    var dist = d as Protobot.Devices.DistanceSensor;
    if (dist != null) sb.Append(" | found " + dist.Found + " mm " + dist.Mm + " size " + dist.Size);
    var opt = d as Protobot.Devices.OpticalSensor;
    if (opt != null) sb.Append(" | hue " + opt.Hue + " prox " + opt.Proximity + " bright " + opt.Brightness + " near " + opt.Near);
    var sh = d as Protobot.Devices.ShaftSensor;
    if (sh != null) sb.Append(" | shaft " + sh.HasShaft + " angle " + sh.Angle + " pos " + sh.Position + " rpm " + sh.Rpm);
    var m = d as Protobot.Devices.MotorDevice;
    if (m != null) sb.Append(" | rpm " + m.Rpm + " deg " + m.Degrees + " torque " + m.Torque);
    var sw = d as Protobot.Devices.SwitchSensor;
    if (sw != null) sb.Append(" | pressed " + sw.Pressed);
    var lt = d as Protobot.Devices.LineTracker;
    if (lt != null) sb.Append(" | value " + lt.Value);
    var ls = d as Protobot.Devices.LightSensor;
    if (ls != null) sb.Append(" | value " + ls.Value);
    var imu = d as Protobot.Devices.InertialSensor;
    if (imu != null) sb.Append(" | heading " + imu.Heading + " accel " + imu.Accel);
    sb.AppendLine();
}
return sb.ToString();
