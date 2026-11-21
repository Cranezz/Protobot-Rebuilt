// The Python stubs VS Code reads (vex.pyi, motion.pyi) against the live API: every declared name must exist
// ("missing"), and every name the simulator has should be declared ("not in stub": no completion, and a module-level
// one gets "is not defined" in VS Code). Needs Play mode with the 6-motor drive loaded.
var prog = @"from vex import *
import motion
brain = Brain()
ctl = Controller()
m = Motor(Ports.PORT1, GearSetting.RATIO_18_1, False)
m2 = Motor(Ports.PORT2)
g = MotorGroup(m, m2)
dt = DriveTrain(g, g, 319.19, 295, 40, MM, 1)
imu = Inertial(Ports.PORT10)
sd = SmartDrive(g, g, imu, 319.19, 320, 40, MM, 1)
di = Distance(Ports.PORT12)
op = Optical(Ports.PORT13)
vis = Vision(Ports.PORT14, 50)
vobj = vis.largest_object()
gp = Gps(Ports.PORT15, 0, 0, MM, 180, RIGHT)
aiv = AiVision(Ports.PORT16, AiVision.ALL_AIOBJS)
aobj = aiv.largest_object()
rot = Rotation(Ports.PORT11)
bu = Bumper(brain.three_wire_port.a)
li = Limit(brain.three_wire_port.b)
din = DigitalIn(brain.three_wire_port.c)
dout = DigitalOut(brain.three_wire_port.d)
pn = Pneumatics(brain.three_wire_port.e)
enc = Encoder(brain.three_wire_port.e)
pot = Potentiometer(brain.three_wire_port.g)
pot2 = PotentiometerV2(brain.three_wire_port.h)
ln = Line(brain.three_wire_port.a)
lt = Light(brain.three_wire_port.b)
so = Sonar(brain.three_wire_port.c)
tm = Timer()
def f():
    pass
th = Thread(f)
ev = Event()
comp = Competition(f, f)
col = Color(255, 0, 0)
mdt = motion.Drivetrain(g, g, 12, motion.Omniwheel.NEW_325, 360, 2)
tw = motion.TrackingWheel(rot, motion.Omniwheel.NEW_2, -2)
sens = motion.OdomSensors(None, None, None, None, imu)
lat = motion.ControllerSettings(10, 0, 3, 3, 1, 100, 3, 500, 20)
cur = motion.ExpoDriveCurve(3, 10, 1.019)
ch = motion.Chassis(mdt, lat, lat, sens, cur, cur)
pose = motion.Pose(1, 2, 3)
gy = Gyro(brain.three_wire_port.d)
acc = Accelerometer(brain.three_wire_port.e, True)
tri = Triport(Ports.PORT7)
m55 = Motor55(Ports.PORT7)
";
var map = new System.Collections.Generic.Dictionary<string, string> {
    { "Ports", "Ports" }, { "GearSetting", "GearSetting" }, { "Color", "col" }, { "FontType", "FontType" }, { "Screen", "brain.screen" }, { "Timer", "tm" },
    { "_ThreeWirePorts", "brain.three_wire_port" }, { "Brain", "brain" }, { "_Axis", "ctl.axis1" }, { "_Button", "ctl.buttonA" }, { "Controller", "ctl" },
    { "Motor", "m" }, { "MotorGroup", "g" }, { "DriveTrain", "dt" }, { "Inertial", "imu" }, { "SmartDrive", "sd" }, { "Distance", "di" }, { "Optical", "op" }, { "Vision", "vis" }, { "_VisionObject", "vobj" }, { "Gps", "gp" }, { "AiVision", "aiv" }, { "_AiVisionObject", "aobj" },
    { "Rotation", "rot" }, { "Bumper", "bu" }, { "Limit", "li" }, { "DigitalIn", "din" }, { "DigitalOut", "dout" }, { "Pneumatics", "pn" }, { "Encoder", "enc" },
    { "Potentiometer", "pot" }, { "PotentiometerV2", "pot2" }, { "Line", "ln" }, { "Light", "lt" }, { "Sonar", "so" }, { "Thread", "th" }, { "Event", "ev" },
    { "Competition", "comp" }, { "DirectionType", "DirectionType" }, { "TurnType", "TurnType" }, { "VelocityUnits", "VelocityUnits" }, { "PercentUnits", "PercentUnits" }, { "RotationUnits", "RotationUnits" }, { "TimeUnits", "TimeUnits" }, { "DistanceUnits", "DistanceUnits" }, { "VoltageUnits", "VoltageUnits" }, { "CurrentUnits", "CurrentUnits" }, { "TorqueUnits", "TorqueUnits" }, { "PowerUnits", "PowerUnits" }, { "TemperatureUnits", "TemperatureUnits" }, { "BrakeType", "BrakeType" }, { "ControllerType", "ControllerType" }, { "AxisType", "AxisType" }, { "OrientationType", "OrientationType" }, { "LedStateType", "LedStateType" }, { "ObjectSizeType", "ObjectSizeType" }, { "Gyro", "gy" }, { "Accelerometer", "acc" }, { "Triport", "tri" }, { "Motor55", "m55" }, { "_Battery", "brain.battery" },
    { "motion.AngularDirection", "motion.AngularDirection" }, { "motion.DriveSide", "motion.DriveSide" }, { "motion.Omniwheel", "motion.Omniwheel" },
    { "motion.Pose", "pose" }, { "motion.Drivetrain", "mdt" }, { "motion.TrackingWheel", "tw" }, { "motion.OdomSensors", "sens" },
    { "motion.ControllerSettings", "lat" }, { "motion.ExpoDriveCurve", "cur" }, { "motion.Chassis", "ch" },
};
var log = new System.Text.StringBuilder();
System.Action<string> h = s => log.Append(s);
Protobot.RobotCode.RobotProgram.Printed += h;
string err; int eline;
if (!Protobot.RobotCode.RobotProgram.Start(prog, Protobot.RobotCode.RobotProgram.Period.Driver, 0, out err, out eline)) return "didn't start: " + err + " line " + eline;
var p = Protobot.RobotCode.RobotProgram.Current;
for (var i = 0; i < 5; i++) Protobot.RobotCode.RobotProgram.Step(1f / 240f);
Protobot.RobotCode.RobotProgram.Printed -= h;
if (p.Failed) return "failed: " + log;
var gl = p.vm.main.globals;
var sb = new System.Text.StringBuilder();
var membersField = typeof(Protobot.RobotCode.VexObject).GetField("members", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
System.Func<string, object> reach = target => {
    var parts = target.Split('.');
    object o = gl[parts[0]];
    for (var k = 1; k < parts.Length; k++) o = p.vm.GetAttr(o, parts[k]);
    return o;
};
foreach (var item in new[] { new[] { "vex.pyi", Protobot.RobotCode.ApiStub.Text, "" }, new[] { "motion.pyi", Protobot.RobotCode.ApiStub.Motion, "motion." } }) {
    var file = item[0]; var text = item[1]; var prefix = item[2];
    string cls = null;
    var declared = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>();
    var top = new System.Collections.Generic.HashSet<string>();
    var bases = new System.Collections.Generic.Dictionary<string, string>();
    var parens = 0;
    foreach (var raw in text.Split('\n')) {
        var line = System.Text.RegularExpressions.Regex.Replace(raw, @"#.*$", "").TrimEnd();
        var cont = parens > 0;
        foreach (var ch in line) { if (ch == '(' || ch == '[') parens++; else if (ch == ')' || ch == ']') parens--; }
        if (cont || line.Trim().Length == 0) continue;
        var c = System.Text.RegularExpressions.Regex.Match(line, @"^class\s+(\w+)(?:\((\w+)\))?");
        if (c.Success) { cls = c.Groups[1].Value; declared[cls] = new System.Collections.Generic.HashSet<string>(); top.Add(cls); if (c.Groups[2].Success) bases[cls] = c.Groups[2].Value; continue; }
        var indented = char.IsWhiteSpace(line[0]);
        if (!indented) cls = null;
        if (line.StartsWith("from ") || line.StartsWith("import ")) continue;
        foreach (var part in line.Split(';')) {
            var d = System.Text.RegularExpressions.Regex.Match(part, @"^\s*def\s+(\w+)");
            var a = System.Text.RegularExpressions.Regex.Match(part, @"^\s*([A-Za-z_]\w*)\s*:");
            var name = d.Success ? d.Groups[1].Value : a.Success ? a.Groups[1].Value : null;
            if (name == null || name.StartsWith("__")) continue;
            if (indented && cls != null) declared[cls].Add(name); else if (!indented) top.Add(name);
        }
    }
    var missing = new System.Collections.Generic.List<string>();
    var extra = new System.Collections.Generic.List<string>();
    var checkedN = 0;
    // A class has its base's members too.
    var all = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>();
    foreach (var kv in declared) {
        var set = new System.Collections.Generic.HashSet<string>(kv.Value);
        string b = kv.Key;
        while (bases.TryGetValue(b, out b) && declared.ContainsKey(b)) set.UnionWith(declared[b]);
        all[kv.Key] = set;
    }
    var helpers = new System.Collections.Generic.HashSet<string> { "Screen" };
    foreach (var kv in all) {
        string target;
        if (!map.TryGetValue(prefix + kv.Key, out target)) { if (kv.Value.Count > 0) missing.Add("(no object for " + kv.Key + ")"); continue; }
        object o;
        try { o = reach(target); } catch (System.Exception e) { missing.Add("(couldn't reach " + target + ": " + e.Message + ")"); continue; }
        foreach (var n in kv.Value) {
            checkedN++;
            var found = false;
            try { p.vm.GetAttr(o, n); found = true; } catch { }
            if (!found && gl.ContainsKey(kv.Key)) try { p.vm.GetAttr(gl[kv.Key], n); found = true; } catch { }   // class constants (Color.RED)
            if (!found) missing.Add(kv.Key + "." + n);
        }
        if (o is Protobot.RobotCode.VexObject vo) {
            var members = (System.Collections.IDictionary)membersField.GetValue(vo);
            foreach (var k in members.Keys) { var s = (string)k; if (!s.StartsWith("_") && !kv.Value.Contains(s) && !(s.Any(char.IsUpper) && kv.Value.Contains(System.Text.RegularExpressions.Regex.Replace(s, "([a-z])([A-Z])", "$1_$2").ToLowerInvariant()))) extra.Add(kv.Key + "." + s); }
        }
    }
    // Module level.
    if (prefix == "") {
        foreach (var n in top) { if (n.StartsWith("_") || helpers.Contains(n)) continue; checkedN++; if (!gl.ContainsKey(n)) missing.Add(n); }
        var mine = new System.Collections.Generic.HashSet<string> { "brain", "ctl", "m", "m2", "g", "dt", "imu", "sd", "di", "op", "rot", "bu", "li", "din", "dout", "pn", "enc", "pot", "pot2", "ln", "lt", "so", "tm", "f", "th", "ev", "comp", "col", "mdt", "tw", "sens", "lat", "cur", "ch", "pose", "motion", "gy", "m55", "vis", "vobj", "gp", "aiv", "aobj", "acc", "tri" };
        foreach (var k in gl.Keys) if (!k.StartsWith("_") && !top.Contains(k) && !mine.Contains(k)) extra.Add("(module) " + k);
    } else {
        var mod = gl["motion"];
        foreach (var n in top) { checkedN++; try { p.vm.GetAttr(mod, n); } catch { missing.Add("motion." + n); } }
    }
    sb.AppendLine(file + ": checked " + checkedN + ", missing " + missing.Count + (missing.Count > 0 ? ": " + string.Join(", ", missing) : ""));
    sb.AppendLine("  not in stub " + extra.Count + ": " + string.Join(", ", extra));
}
Protobot.RobotCode.RobotProgram.Stop(null);
return sb.ToString();
