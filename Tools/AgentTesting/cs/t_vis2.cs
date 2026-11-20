// (Play mode, after t_vis1.cs) runs a VEXcode Python, a VEXcode C++ and a PROS program against the Vision Sensor
// t_vis1 set up (no simulation: the program is stepped here), with the signatures t_vis1 made ("t_vis_sigs"), and
// prints what each sees. The sensor's port comes from the Brain.
var sensor = Protobot.Devices.V5Brain.Devices.OfType<Protobot.Devices.VisionSensor>().FirstOrDefault();
if (sensor == null) return "run t_vis1.cs first";
var port = sensor.port;
var parts = UnityEngine.PlayerPrefs.GetString("t_vis_sigs").Split('|');
var red = Protobot.Devices.VisionSignature.Parse(parts[0]).Value;
var blue = Protobot.Devices.VisionSignature.Parse(parts[1]).Value;
System.Func<Protobot.Devices.VisionSignature, string> V = s => string.Join(", ", s.uMin, s.uMax, s.uMean, s.vMin, s.vMax, s.vMean, s.range.ToString(System.Globalization.CultureInfo.InvariantCulture), s.type);
var py = "from vex import *\n" +
    "RED = Signature(1, " + V(red) + ")\n" +
    "BLUE = Signature(2, " + V(blue) + ")\n" +
    "BOTH = Code(RED, BLUE)\n" +
    "vision_1 = Vision(Ports.PORT" + port + ", 50, RED, BLUE, BOTH)\n" +
    "wait(100, MSEC)\n" +
    "reds = vision_1.take_snapshot(RED)\n" +
    "print('py red', len(reds), vision_1.largest_object().centerX, vision_1.largest_object().centerY, vision_1.largest_object().width, vision_1.object_count)\n" +
    "blues = vision_1.take_snapshot(BLUE)\n" +
    "print('py blue', len(blues), blues[0].centerX if blues else -1, blues[0].exists if blues else False)\n" +
    "print('py none', len(vision_1.take_snapshot(Signature(3, 1, 2, 1, 1, 2, 1, 2.5, 0))))\n";
var cpp = "#include \"vex.h\"\nusing namespace vex;\n" +
    "vision::signature RED = vision::signature(1, " + V(red) + ");\n" +
    "vision Vision1 = vision(PORT" + port + ", 50, RED);\n" +
    "int main() {\n  wait(100, msec);\n  int n = Vision1.takeSnapshot(RED);\n" +
    "  printf(\"cpp red %d %d %d %d\\n\", n, Vision1.largestObject.centerX, Vision1.objects[0].width, (int)Vision1.objects[1].exists);\n  return 0;\n}\n";
var pros = "#include \"main.h\"\n" +
    "pros::Vision vis(" + port + ");\n" +
    "void initialize() {\n  pros::delay(100);\n" +
    "  pros::vision_object_s_t o = vis.get_by_sig(0, 1);\n" +
    "  printf(\"pros red %d %d %d %d\\n\", o.signature, o.x_middle_coord, o.width, vis.get_object_count());\n" +
    "  pros::vision_object_s_t b = vis.get_by_size(1);\n" +
    "  printf(\"pros second %d %d\\n\", b.signature, b.x_middle_coord);\n" +
    "  pros::vision_object_s_t none = vis.get_by_sig(0, 5);\n" +
    "  printf(\"pros none %d\\n\", none.signature);\n" +
    "  vis.set_zero_point(pros::E_VISION_ZERO_CENTER);\n" +
    "  printf(\"pros centered %d\\n\", vis.get_by_sig(0, 1).x_middle_coord);\n}\n";
var output = new System.Text.StringBuilder();
System.Action<string> listen = s => output.Append(s);
Protobot.RobotCode.RobotProgram.Printed += listen;
foreach (var program in new[] { py, cpp, pros }) {
    string err; int line;
    if (!Protobot.RobotCode.RobotProgram.Start(program, Protobot.RobotCode.RobotProgram.Period.Program, 0, out err, out line)) { output.AppendLine("ERROR " + err); continue; }
    for (var i = 0; i < 200 && Protobot.RobotCode.RobotProgram.Current != null && Protobot.RobotCode.RobotProgram.Current.Status == "Running"; i++) Protobot.RobotCode.RobotProgram.Step(0.01f);
    output.AppendLine("[" + (Protobot.RobotCode.RobotProgram.Current != null ? Protobot.RobotCode.RobotProgram.Current.Status : "none") + "]");
    Protobot.RobotCode.RobotProgram.Stop(null);
}
Protobot.RobotCode.RobotProgram.Printed -= listen;
return output.ToString();
