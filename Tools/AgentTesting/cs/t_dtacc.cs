// (Play mode, the 6-motor drive loaded) DriveTrain drive_for 24" then turn_for 90°; the omni hold gain and gearbox
// friction from PlayerPrefs "t_dtacc" = "gain,friction" (empty: leave them). Read the marks with t_marks.cs
// ("t_code_marks": A start, B after the drive, C after the turn). Track 14.125" = the 6-motor drive's, as Insert
// devices measures it (12.5" here made the turn look like 76°: the wheels turned for a narrower robot).
var BF = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var set = UnityEngine.PlayerPrefs.GetString("t_dtacc", "");
var gain = typeof(Protobot.Motion.Sandbox).GetField("HoldGain", BF);
var friction = typeof(Protobot.Motion.Sandbox).GetField("GearboxFriction", BF);
if (set != "") { var v = set.Split(','); gain.SetValue(null, float.Parse(v[0])); friction.SetValue(null, float.Parse(v[1])); }
var code = @"from vex import *
brain = Brain()
left = MotorGroup(Motor(Ports.PORT4), Motor(Ports.PORT5), Motor(Ports.PORT6))
right = MotorGroup(Motor(Ports.PORT1, True), Motor(Ports.PORT2, True), Motor(Ports.PORT3, True))
dt = DriveTrain(left, right, 3.25 * 3.14159, 14.125, 10, INCHES, 60 / 36)
dt.set_drive_velocity(50, PERCENT)
dt.set_turn_velocity(30, PERCENT)
dt.set_stopping(BRAKE)
wait(0.5, SECONDS)
print('mark A', brain.timer.time(SECONDS))
dt.drive_for(FORWARD, 24, INCHES)
wait(0.5, SECONDS)
print('mark B', brain.timer.time(SECONDS))
dt.turn_for(RIGHT, 90, DEGREES)
wait(0.5, SECONDS)
print('mark C', brain.timer.time(SECONDS))
";
Protobot.Motion.Sandbox.Stop(); Protobot.Motion.Sandbox.Begin();
var rb = Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb;
var marks = new System.Collections.Generic.List<string>();
UnityEngine.PlayerPrefs.SetString("t_code_marks", "");
Protobot.RobotCode.RobotProgram.Printed += s => { if (s.StartsWith("mark") && rb != null) { marks.Add(s.Trim().Split(' ')[1] + " z=" + rb.position.z.ToString("0.00") + " x=" + rb.position.x.ToString("0.00") + " yaw=" + rb.rotation.eulerAngles.y.ToString("0.0")); UnityEngine.PlayerPrefs.SetString("t_code_marks", string.Join(" | ", marks)); } };
string err; int line;
var ok = Protobot.RobotCode.RobotProgram.Start(code, Protobot.RobotCode.RobotProgram.Period.Program, 0, out err, out line);
return "started " + ok + " " + err + " gain " + gain.GetValue(null) + " friction " + friction.GetValue(null);

