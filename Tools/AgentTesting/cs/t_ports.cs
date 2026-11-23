// (Play, a build with motors and sensors loaded) Port picker: swaps the first two motors' ports, moves a smart sensor
// to a free port, moves a 3-wire device, then saves and reopens the build (SceneBuild.ToBuildData -> GenerateBuild) and
// checks the ports stayed. Temp/ports.txt: "set" | "reload" | "show".
var cmd = System.IO.File.Exists("Temp/ports.txt") ? System.IO.File.ReadAllText("Temp/ports.txt").Trim() : "show";
var B = typeof(Protobot.Devices.V5Brain);
Protobot.Devices.V5Brain.Refresh(true);
var devs = Protobot.Devices.V5Brain.Devices.ToList();
var sb = new System.Text.StringBuilder();
if (cmd == "set") {
    var motors = devs.Where(d => d.kind == Protobot.Devices.DeviceKind.Motor).ToList();
    var a = motors[0]; var b = motors[1];
    sb.AppendLine("before: " + a.Name + " " + a.PortLabel + ", " + b.Name + " " + b.PortLabel);
    var bPort = b.CodePort;
    sb.AppendLine("swap: " + (Protobot.Devices.V5Brain.SetPort(a, bPort) ?? "ok"));
    var sensor = devs.FirstOrDefault(d => !d.threeWire && d.kind != Protobot.Devices.DeviceKind.Motor);
    if (sensor != null) sb.AppendLine("sensor " + sensor.Name + " to 20: " + (Protobot.Devices.V5Brain.SetPort(sensor, 20) ?? "ok"));
    var three = Protobot.Devices.V5Brain.Devices.FirstOrDefault(d => d.threeWire && d.part != null);
    if (three != null) sb.AppendLine("3-wire " + three.Name + " to G: " + (Protobot.Devices.V5Brain.SetPort(three, 7) ?? "ok"));
    sb.AppendLine("choices for a motor: " + Protobot.Devices.V5Brain.PortChoices(a).Count + ", first: " + Protobot.Devices.V5Brain.PortChoices(a)[0].label);
}
if (cmd == "reload") {
    var data = Protobot.Builds.SceneBuild.ToBuildData();
    Protobot.Builds.SceneBuild.GenerateBuild(data);
    sb.AppendLine("reloaded " + data.parts.Length + " parts");
}
foreach (var d in Protobot.Devices.V5Brain.Devices) sb.AppendLine("  " + d.PortLabel + "  " + d.Name + (d.wired ? " (cable)" : ""));
return sb.ToString();
