// Every function/member the VS Code headers declare must exist in the simulator: runs a program per header that makes
// one of each object, then looks each declared name up on it (GetAttr). Needs Play mode (the 6-motor drive loaded).
var dir = @"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Assets/ProtobotRebuilt/Resources/Robot Code Headers/";
var sb = new System.Text.StringBuilder();
var keywords = new System.Collections.Generic.HashSet<string> { "if", "while", "for", "return", "sizeof", "decltype", "defined", "__has_include", "operator", "template", "static_assert" };

// header -> (program, scope path -> object path in the program's globals)
var progs = new System.Collections.Generic.Dictionary<string, string>();
var maps = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>>();
progs["cpp_pros"] = "#include \"main.h\"\n" +
    "pros::Motor m(1, pros::MotorGears::blue);\npros::MotorGroup g({1, -2});\npros::Controller c(pros::E_CONTROLLER_MASTER);\npros::Imu imu(10);\n" +
    "pros::Rotation r(11);\npros::Distance d(12);\npros::Optical o(13);\npros::Vision vis(14);\npros::adi::DigitalOut dout('A');\npros::adi::Pneumatics pn('B', false);\n" +
    "pros::adi::DigitalIn din('C');\npros::adi::Encoder enc('E', 'F');\nvoid f() { pros::delay(10); }\npros::Task t(f);\n" +
    "void opcontrol() { pros::delay(10); }\n";
maps["cpp_pros"] = new System.Collections.Generic.Dictionary<string, string> {
    { "pros.AbstractMotor", "m" }, { "pros.Motor", "m" }, { "pros.MotorGroup", "g" }, { "pros.Controller", "c" }, { "pros.Imu", "imu" },
    { "pros.Rotation", "r" }, { "pros.Distance", "d" }, { "pros.Optical", "o" }, { "pros.Vision", "vis" }, { "pros.adi.DigitalOut", "dout" }, { "pros.adi.Pneumatics", "pn" },
    { "pros.adi.DigitalIn", "din" }, { "pros.adi.Encoder", "enc" }, { "pros.Task", "t" }, { "pros", "pros" }, { "pros.adi", "pros.adi" },
    { "pros.lcd", "pros.lcd" }, { "pros.competition", "pros.competition" },
};
progs["cpp_vex"] = "#include \"vex.h\"\nusing namespace vex;\n" +
    "motor m(PORT1, ratio18_1, false);\nmotor m2(PORT2);\nmotor_group g(m, m2);\ndrivetrain dt(g, g, 319, 295, 40, mm, 1);\ninertial imu(PORT10);\n" +
    "smartdrive sd(g, g, imu, 319, 320, 40, mm, 1);\nrotation r(PORT11);\ndistance di(PORT12);\noptical o(PORT13);\nvision vis(PORT14, 50);\nbumper b(Brain.ThreeWirePort.A);\n" +
    "limit l(Brain.ThreeWirePort.B);\ndigital_out dout(Brain.ThreeWirePort.C);\ndigital_in din(Brain.ThreeWirePort.D);\npneumatics pn(Brain.ThreeWirePort.E);\n" +
    "encoder enc(Brain.ThreeWirePort.F);\npot pt(Brain.ThreeWirePort.G);\npotV2 pt2(Brain.ThreeWirePort.H);\nline ln(Brain.ThreeWirePort.A);\nlight lt(Brain.ThreeWirePort.B);\n" +
    "sonar so(Brain.ThreeWirePort.C);\ngyro gy(Brain.ThreeWirePort.D);\ntimer tm;\ncompetition comp;\nvoid f() { }\nint fi() { return 0; }\nthread th(f);\ntask tk(fi);\nevent ev(f);\n" +
    "color col(255, 0, 0);\nint main() { waitUntil(true); repeat(2) { wait(5, msec); } return 0; }\n";
maps["cpp_vex"] = new System.Collections.Generic.Dictionary<string, string> {
    { "vex", "vex" }, { "vex.color", "col" }, { "vex.triport", "Brain.ThreeWirePort" }, { "vex.brain", "Brain" }, { "vex.brain.lcd", "Brain.Screen" },
    { "vex.brain.timer_", "Brain.Timer" }, { "vex.brain.battery", "Brain.Battery" }, { "vex.controller", "Controller1" }, { "vex.controller.axis", "Controller1.Axis1" },
    { "vex.controller.button", "Controller1.ButtonA" }, { "vex.controller.lcd", "Controller1.Screen" }, { "vex.motor", "m" }, { "vex.motor_group", "g" },
    { "vex.inertial", "imu" }, { "vex.rotation", "r" }, { "vex.distance", "di" }, { "vex.optical", "o" }, { "vex.vision", "vis" }, { "vex.vision.object", "vis.largestObject" }, { "vex.bumper", "b" }, { "vex.limit", "l" },
    { "vex.digital_out", "dout" }, { "vex.digital_in", "din" }, { "vex.pneumatics", "pn" }, { "vex.encoder", "enc" }, { "vex.pot", "pt" }, { "vex.potV2", "pt2" },
    { "vex.line", "ln" }, { "vex.light", "lt" }, { "vex.sonar", "so" }, { "vex.gyro", "gy" }, { "vex.drivetrain", "dt" }, { "vex.smartdrive", "sd" },
    { "vex.timer", "tm" }, { "vex.thread", "th" }, { "vex.task", "tk" }, { "vex.this_thread", "vex.this_thread" }, { "vex.event", "ev" }, { "vex.competition", "comp" },
};
progs["cpp_lemlib"] = "#include \"main.h\"\n" +
    "pros::MotorGroup L({-4, -5, -6}, pros::MotorGears::blue);\npros::MotorGroup R({1, 2, 3}, pros::MotorGears::blue);\npros::Imu imu(10);\n" +
    "lemlib::Drivetrain dt(&L, &R, 10, lemlib::Omniwheel::NEW_325, 360, 2);\npros::Rotation rot(11);\nlemlib::TrackingWheel tw(&rot, lemlib::Omniwheel::NEW_2, -2.5);\n" +
    "lemlib::OdomSensors s(nullptr, nullptr, nullptr, nullptr, &imu);\nlemlib::ControllerSettings lat(10, 0, 3, 3, 1, 100, 3, 500, 20);\n" +
    "lemlib::ControllerSettings ang(2, 0, 10, 3, 1, 100, 3, 500, 0);\nlemlib::ExpoDriveCurve cur(3, 10, 1.019);\nlemlib::Chassis ch(dt, lat, ang, s, &cur, &cur);\n" +
    "lemlib::Pose pose(0, 0, 0);\nvoid opcontrol() { pros::delay(10); }\n";
maps["cpp_lemlib"] = new System.Collections.Generic.Dictionary<string, string> {
    { "lemlib", "lemlib" }, { "lemlib.Chassis", "ch" }, { "lemlib.DriveCurve", "cur" }, { "lemlib.ExpoDriveCurve", "cur" }, { "lemlib.TelemetrySink", "lemlib.telemetrySink()" },
};
progs["cpp_ez"] = "#include \"main.h\"\n" +
    "ez::Drive chassis({-1, -2, -3}, {4, 5, 6}, 10, 3.25, 360);\nez::tracking_wheel tw(8, 2.75, 4.0);\nvoid opcontrol() { pros::delay(10); }\n";
maps["cpp_ez"] = new System.Collections.Generic.Dictionary<string, string> {
    { "ez", "ez" }, { "ez.Drive", "chassis" }, { "ez.tracking_wheel", "tw" }, { "ez.as", "ez.as" }, { "ez.as.AutonSelector", "ez.as.auton_selector" }, { "ez.util", "ez.util" },
};

var log = new System.Text.StringBuilder();
System.Action<string> h = s => log.Append(s);
Protobot.RobotCode.RobotProgram.Printed += h;
foreach (var header in new[] { "cpp_pros", "cpp_vex", "cpp_lemlib", "cpp_ez" }) {
    log.Clear();
    string err; int eline;
    var ok = Protobot.RobotCode.RobotProgram.Start(progs[header], Protobot.RobotCode.RobotProgram.Period.Driver, 0, out err, out eline);
    if (!ok) { sb.AppendLine(header + ": program didn't start: " + err); continue; }
    var p = Protobot.RobotCode.RobotProgram.Current;
    for (var i = 0; i < 5; i++) Protobot.RobotCode.RobotProgram.Step(1f / 240f);
    if (p.Failed) { sb.AppendLine(header + ": program failed: " + log); continue; }
    var g = p.vm.main.globals;
    // Scan the header.
    var lines = System.IO.File.ReadAllLines(dir + header + ".txt");
    var scopes = new System.Collections.Generic.List<string[]>();   // kind, name, depth
    var depth = 0;
    var parens = 0;
    var checkedCount = 0;
    var missing = new System.Collections.Generic.List<string>();
    foreach (var raw in lines) {
        var line = System.Text.RegularExpressions.Regex.Replace(raw, @"//.*$|/\*.*?\*/", "");
        if (line.TrimStart().StartsWith("*") || line.TrimStart().StartsWith("#")) continue;
        var top = scopes.Count > 0 ? scopes[scopes.Count - 1] : null;
        var direct = top != null && depth == int.Parse(top[2]) + 1;
        var open = System.Text.RegularExpressions.Regex.Match(line, @"^\s*(class|struct|namespace)\s+(\w+)[^;]*\{");
        var names = new System.Collections.Generic.List<string>();
        var startParens = parens;
        foreach (var ch in line) { if (ch == '(') parens++; else if (ch == ')') parens--; }
        if (startParens == 0 && direct && !open.Success && !line.Contains("static ") && top[0] != "struct" && !line.Contains("operator") && !System.Text.RegularExpressions.Regex.IsMatch(line, @"^\s*(typedef|using|enum|constexpr|const |static const|friend|extern|public:|private:)")) {
            var fn = System.Text.RegularExpressions.Regex.Match(line, @"(?:^|[\s\*&:>])(\w+)\s*\(");
            if (fn.Success && !keywords.Contains(fn.Groups[1].Value)) {
                if (fn.Groups[1].Value != top[1] && !(top[0] == "namespace" && line.TrimStart().StartsWith(top[1]))) names.Add(fn.Groups[1].Value);
            }
            else if (!line.Contains("(")) {
                var field = System.Text.RegularExpressions.Regex.Match(line, @"^\s*[\w:<>\*&]+\s+([A-Za-z_]\w*(?:\s*,\s*[A-Za-z_]\w*)*)\s*(=[^;]*)?;");
                if (field.Success && top[0] != "namespace") foreach (var n in field.Groups[1].Value.Split(',')) names.Add(n.Trim());
            }
        }
        if (names.Count > 0) {
            var path = string.Join(".", scopes.Select(s => s[1]));
            string target;
            if (!maps[header].TryGetValue(path, out target)) { foreach (var n in names) missing.Add("(no object mapped for " + path + ") " + n); }
            else {
                object obj = null;
                try {
                    var parts = target.Split('.');
                    var first = parts[0].Replace("()", "");
                    obj = g[first];
                    if (parts[0].EndsWith("()")) obj = p.vm.CallSync(obj);
                    for (var k = 1; k < parts.Length; k++) { var nm = parts[k].Replace("()", ""); obj = p.vm.GetAttr(obj, nm); if (parts[k].EndsWith("()")) obj = p.vm.CallSync(obj); }
                }
                catch (System.Exception e) { missing.Add("(couldn't reach " + target + ": " + e.Message + ")"); obj = null; }
                if (obj != null)
                    foreach (var n in names) {
                        checkedCount++;
                        var before = log.Length;
                        try { p.vm.GetAttr(obj, n); if (log.ToString(before, log.Length - before).Contains("isn't simulated")) missing.Add(path + "::" + n + " (EZ: not simulated)"); }
                        catch (System.Exception e) { missing.Add(path + "::" + n); }
                    }
            }
        }
        if (open.Success) scopes.Add(new[] { open.Groups[1].Value, open.Groups[2].Value, depth.ToString() });
        foreach (var ch in line) {
            if (ch == '{') depth++;
            else if (ch == '}') {
                depth--;
                if (scopes.Count > 0 && depth == int.Parse(scopes[scopes.Count - 1][2])) scopes.RemoveAt(scopes.Count - 1);
            }
        }
    }
    sb.AppendLine(header + ": checked " + checkedCount + ", missing " + missing.Count + (missing.Count > 0 ? ": " + string.Join(", ", missing.Distinct()) : ""));
}
Protobot.RobotCode.RobotProgram.Printed -= h;
Protobot.RobotCode.RobotProgram.Stop(null);
return sb.ToString();
