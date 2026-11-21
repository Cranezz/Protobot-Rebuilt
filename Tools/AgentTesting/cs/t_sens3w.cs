// (Play mode, a build loaded) Every 3-wire sensor plus a V5 3-Wire Expander put beside the robot (13 ports: the ones
// past H go on the expander), then each read from Python, VEXcode C++ and PROS (no simulation: the build's readings).
// Shows the devices' ports, Insert devices' lines and each program's output.
var sb = new System.Text.StringBuilder();
foreach (var old in UnityEngine.Object.FindObjectsByType<Transform>(UnityEngine.FindObjectsSortMode.None).Where(t => t != null && t.name.StartsWith("Sens3W")).ToList()) UnityEngine.Object.Destroy(old.gameObject);
var ids = new[] { "VXSENSORS-V5 3 Wire Expander", "SNSR-Line Tracker-Cortex", "VXSENSORS-Light Sensor", "VXSENSORS-Potentiometer V2", "VXSENSORS-Ultrasonic Range Finder",
    "VXSENSORS-Yaw Rate Gyroscope Sensor", "VXSENSORS-Analog Accelerometer", "VXSENSORS-Optical Shaft Encoder", "SWCH-Bumper-Cortex", "SWCH-Limit-Cortex" };
var x = -12f;
foreach (var id in ids) {
    var p = Protobot.PartsManager.GeneratePart(id, new Vector3(x, 6f, 14f), Quaternion.identity);
    if (p == null) { sb.AppendLine("no part " + id); continue; }
    p.name = "Sens3W " + p.name;
    x += 3f;
}
UnityEngine.Physics.SyncTransforms();
Protobot.Devices.V5Brain.Refresh(true);
var three = Protobot.Devices.V5Brain.Devices.Where(d => d.threeWire && !(d is Protobot.Devices.SolenoidDevice)).ToList();
var expander = Protobot.Devices.V5Brain.Devices.FirstOrDefault(d => d.kind == Protobot.Devices.DeviceKind.Expander);
sb.AppendLine("expander on " + (expander != null ? expander.port.ToString() : "none") + " | " + string.Join(", ", three.Select(d => d.kind + " " + d.PortLabel)));
System.Func<Protobot.Devices.V5Device, int, string> L = (d, i) => ((char)('A' + d.port - 1 + i)).ToString();
System.Func<Protobot.Devices.V5Device, int, string> Py = (d, i) => (d.expander > 0 ? "Triport(Ports.PORT" + d.expander + ")" : "brain.three_wire_port") + "." + L(d, i).ToLower();
System.Func<Protobot.Devices.V5Device, int, string> Vx = (d, i) => (d.expander > 0 ? "triport(PORT" + d.expander + ")" : "Brain.ThreeWirePort") + "." + L(d, i);
System.Func<Protobot.Devices.V5Device, int, string> Pr = (d, i) => d.expander > 0 ? "{{" + d.expander + ", '" + L(d, i) + "'}}" : "'" + L(d, i) + "'";
System.Func<Protobot.Devices.V5Device, string> Pr2 = d => d.expander > 0 ? "{{" + d.expander + ", '" + L(d, 0) + "', '" + L(d, 1) + "'}}" : "'" + L(d, 0) + "', '" + L(d, 1) + "'";
var py = new System.Text.StringBuilder("from vex import *\nbrain = Brain()\n");
var vx = new System.Text.StringBuilder("#include \"vex.h\"\nusing namespace vex;\nbrain Brain;\nint main() {\n");
var pr = new System.Text.StringBuilder("#include \"main.h\"\nvoid initialize() {\n");
var k = 0;
foreach (var d in three) {
    var n = "s" + (k++);
    switch (d.kind) {
        case Protobot.Devices.DeviceKind.LineTracker:
            py.AppendLine("print('line', Line(" + Py(d, 0) + ").reflectivity())");
            vx.AppendLine("  line " + n + " = line(" + Vx(d, 0) + "); printf(\"line %.1f\\n\", " + n + ".reflectivity());");
            pr.AppendLine("  pros::adi::LineSensor " + n + "(" + Pr(d, 0) + "); printf(\"line %d\\n\", " + n + ".get_value());");
            break;
        case Protobot.Devices.DeviceKind.Light:
            py.AppendLine("print('light', Light(" + Py(d, 0) + ").brightness())");
            vx.AppendLine("  light " + n + " = light(" + Vx(d, 0) + "); printf(\"light %.1f\\n\", " + n + ".brightness());");
            pr.AppendLine("  pros::adi::LightSensor " + n + "(" + Pr(d, 0) + "); printf(\"light %d\\n\", " + n + ".get_value());");
            break;
        case Protobot.Devices.DeviceKind.Potentiometer:
            py.AppendLine("print('pot', PotentiometerV2(" + Py(d, 0) + ").angle())");
            vx.AppendLine("  potV2 " + n + " = potV2(" + Vx(d, 0) + "); printf(\"pot %.1f\\n\", " + n + ".angle(degrees));");
            pr.AppendLine("  pros::adi::Potentiometer " + n + "(" + Pr(d, 0) + ", pros::E_ADI_POT_V2); printf(\"pot %.1f %d\\n\", " + n + ".get_angle(), " + n + ".get_value());");
            break;
        case Protobot.Devices.DeviceKind.Ultrasonic:
            py.AppendLine("print('sonar', Sonar(" + Py(d, 0) + ").distance(MM), Sonar(" + Py(d, 0) + ").found_object())");
            vx.AppendLine("  sonar " + n + " = sonar(" + Vx(d, 0) + "); printf(\"sonar %.1f\\n\", " + n + ".distance(mm));");
            pr.AppendLine("  pros::adi::Ultrasonic " + n + "(" + Pr2(d) + "); printf(\"sonar %d\\n\", " + n + ".get_value());");
            break;
        case Protobot.Devices.DeviceKind.Gyro:
            py.AppendLine("print('gyro', Gyro(" + Py(d, 0) + ").heading())");
            vx.AppendLine("  gyro " + n + " = gyro(" + Vx(d, 0) + "); printf(\"gyro %.1f\\n\", " + n + ".heading(degrees));");
            pr.AppendLine("  pros::adi::Gyro " + n + "(" + Pr(d, 0) + "); printf(\"gyro %.1f\\n\", " + n + ".get_value());");
            break;
        case Protobot.Devices.DeviceKind.Accelerometer:
            py.AppendLine("print('accel', Accelerometer(" + Py(d, 0) + ", True).acceleration(), Accelerometer(" + Py(d, 1) + ", True).acceleration(), Accelerometer(" + Py(d, 2) + ", True).acceleration())");
            vx.AppendLine("  accelerometer " + n + " = accelerometer(" + Vx(d, 2) + ", true); printf(\"accel z %.2f\\n\", " + n + ".acceleration());");
            pr.AppendLine("  pros::adi::AnalogIn " + n + "(" + Pr(d, 2) + "); printf(\"accel z %d\\n\", " + n + ".get_value());");
            break;
        case Protobot.Devices.DeviceKind.Encoder:
            py.AppendLine("print('encoder', Encoder(" + Py(d, 0) + ").position(DEGREES))");
            vx.AppendLine("  encoder " + n + " = encoder(" + Vx(d, 0) + "); printf(\"encoder %.1f\\n\", " + n + ".position(degrees));");
            pr.AppendLine("  pros::adi::Encoder " + n + "(" + Pr2(d) + ", false); printf(\"encoder %d\\n\", " + n + ".get_value());");
            break;
        case Protobot.Devices.DeviceKind.Bumper:
        case Protobot.Devices.DeviceKind.Limit:
            var cls = d.kind == Protobot.Devices.DeviceKind.Bumper ? "Bumper" : "Limit";
            py.AppendLine("print('" + cls.ToLower() + "', " + cls + "(" + Py(d, 0) + ").pressing())");
            vx.AppendLine("  " + cls.ToLower() + " " + n + " = " + cls.ToLower() + "(" + Vx(d, 0) + "); printf(\"" + cls.ToLower() + " %d\\n\", (int)" + n + ".pressing());");
            pr.AppendLine("  pros::adi::DigitalIn " + n + "(" + Pr(d, 0) + "); printf(\"" + cls.ToLower() + " %d\\n\", " + n + ".get_value());");
            break;
    }
}
vx.AppendLine("  return 0;\n}");
pr.AppendLine("}");
var output = new System.Text.StringBuilder();
System.Action<string> listen = s => output.Append(s);
Protobot.RobotCode.RobotProgram.Printed += listen;
var labels = new[] { "PY", "VEXCODE C++", "PROS" };
var programs = new[] { py.ToString(), vx.ToString(), pr.ToString() };
for (var j = 0; j < 3; j++) {
    var label = labels[j]; var program = programs[j];
    output.AppendLine("== " + label);
    string err; int line;
    if (!Protobot.RobotCode.RobotProgram.Start(program, Protobot.RobotCode.RobotProgram.Period.Program, 0, out err, out line)) { output.AppendLine("ERROR " + err + " line " + line); continue; }
    for (var i = 0; i < 100 && Protobot.RobotCode.RobotProgram.Current != null && Protobot.RobotCode.RobotProgram.Current.Status == "Running"; i++) Protobot.RobotCode.RobotProgram.Step(0.01f);
    output.AppendLine("[" + (Protobot.RobotCode.RobotProgram.Current != null ? Protobot.RobotCode.RobotProgram.Current.Status : "none") + "]");
    Protobot.RobotCode.RobotProgram.Stop(null);
}
Protobot.RobotCode.RobotProgram.Printed -= listen;
var cfg = Protobot.RobotCode.DeviceConfig.Generate();
sb.AppendLine("-- Insert devices (Python), 3-wire part:\n" + string.Join("\n", cfg.Split('\n').Where(l => l.Contains("three_wire") || l.Contains("expander") || l.Contains("Accelerometer"))));
var cpp = Protobot.RobotCode.DeviceConfig.GenerateCpp(true);
sb.AppendLine("-- Insert devices (PROS):\n" + string.Join("\n", cpp.Split('\n').Where(l => l.Contains("adi::"))));
var vcfg = Protobot.RobotCode.DeviceConfig.GenerateCpp(false);
sb.AppendLine("-- Insert devices (VEXcode C++):\n" + string.Join("\n", vcfg.Split('\n').Where(l => l.Contains("ThreeWirePort") || l.Contains("Expander") || l.Contains("accelerometer"))));
return sb.ToString() + output;
