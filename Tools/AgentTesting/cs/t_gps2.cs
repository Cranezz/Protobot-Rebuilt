// (Play mode, after t_gps1.cs) runs a VEXcode Python, a VEXcode C++ and a PROS program against the GPS Sensor
// t_gps1 set up (stepped here, no simulation) and prints what each reads, with the sensor's own values to compare.
var gps = Protobot.Devices.V5Brain.Devices.OfType<Protobot.Devices.GpsSensor>().FirstOrDefault();
if (gps == null) return "run t_gps1.cs first";
for (var i = 0; i < 3; i++) gps.Sample(0.02f);
var port = gps.port;
var sb = new System.Text.StringBuilder();
sb.AppendLine("sensor x " + gps.X.ToString("F2") + " y " + gps.Y.ToString("F2") + " h " + gps.Heading.ToString("F1") + " q " + gps.Quality);
var py = "from vex import *\n" +
    "g = Gps(Ports.PORT" + port + ")\n" +
    "o = Gps(Ports.PORT" + port + ", 0, 100, MM, 90, RIGHT)\n" +
    "wait(50, MSEC)\n" +
    "print('py', round(g.x_position(INCHES), 2), round(g.y_position(INCHES), 2), round(g.heading(), 1), g.quality(), g.installed())\n" +
    "print('py offset', round(o.x_position(MM), 1), round(o.y_position(MM), 1), round(o.heading(), 1))\n" +
    "g.set_location(10, 20, INCHES, 45, DEGREES)\n" +
    "print('py set_location (ignored while it sees the strip)', round(g.x_position(INCHES), 2), round(g.heading(), 1))\n";
var cpp = "#include \"vex.h\"\nusing namespace vex;\n" +
    "gps G = gps(PORT" + port + ", 0.0, 0.0, mm, 0, right);\n" +
    "int main() {\n  wait(50, msec);\n" +
    "  printf(\"cpp %.2f %.2f %.1f %d\\n\", G.xPosition(inches), G.yPosition(inches), G.heading(), (int)G.quality());\n  return 0;\n}\n";
var pros = "#include \"main.h\"\n" +
    "pros::Gps gps(" + port + ", 0, 0);\n" +
    "void initialize() {\n  pros::delay(50);\n" +
    "  pros::gps_position_s_t p = gps.get_position();\n" +
    "  printf(\"pros %.3f %.3f %.1f %.3f\\n\", p.x, p.y, gps.get_heading(), gps.get_error());\n" +
    "  pros::gps_status_s_t s = gps.get_status();\n" +
    "  printf(\"pros status %.3f %.3f yaw %.1f x %.3f\\n\", s.x, s.y, s.yaw, gps.get_position_x());\n}\n";
var output = new System.Text.StringBuilder();
System.Action<string> listen = s => output.Append(s);
Protobot.RobotCode.RobotProgram.Printed += listen;
foreach (var program in new[] { py, cpp, pros }) {
    string err; int line;
    if (!Protobot.RobotCode.RobotProgram.Start(program, Protobot.RobotCode.RobotProgram.Period.Program, 0, out err, out line)) { output.AppendLine("ERROR " + err + " line " + line); continue; }
    for (var i = 0; i < 200 && Protobot.RobotCode.RobotProgram.Current != null && Protobot.RobotCode.RobotProgram.Current.Status == "Running"; i++) Protobot.RobotCode.RobotProgram.Step(0.01f);
    output.AppendLine("[" + (Protobot.RobotCode.RobotProgram.Current != null ? Protobot.RobotCode.RobotProgram.Current.Status : "none") + "]");
    Protobot.RobotCode.RobotProgram.Stop(null);
}
Protobot.RobotCode.RobotProgram.Printed -= listen;
return sb.ToString() + output;
