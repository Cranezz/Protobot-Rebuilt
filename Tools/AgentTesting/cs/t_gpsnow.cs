// Prints the GPS Sensor's reading, its true lens position, and the robot's biggest body now.
var gps = Protobot.Devices.V5Brain.Devices.OfType<Protobot.Devices.GpsSensor>().FirstOrDefault();
if (gps == null) return "no gps";
var lens = gps.Lens();
var t = Protobot.Devices.GpsSensor.ToField(lens.origin);
var rb = Protobot.Motion.Sandbox.Instance != null ? Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb : null;
var body = Protobot.Motion.Sandbox.BodyOf(gps.part);
return "gps x " + gps.X.ToString("0.0") + " y " + gps.Y.ToString("0.0") + " h " + gps.Heading.ToString("0") + " q " + gps.Quality + " | lens " + t.ToString("F1")
    + " | robot " + (rb != null ? rb.position.ToString("F1") + " yaw " + rb.rotation.eulerAngles.y.ToString("0") : "-") + " | gps body " + (body != null ? body.name + " mass " + body.mass.ToString("0.000") : "none")
    + "\nprogram " + (Protobot.RobotCode.RobotProgram.Current != null ? Protobot.RobotCode.RobotProgram.Current.Status : "none");
