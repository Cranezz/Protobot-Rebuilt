var code = Protobot.RobotCode.DeviceConfig.Generate() + @"
drivetrain.set_stopping(BRAKE)
brain.timer.clear()
wait(0.5, SECONDS)
print('mark A')
drivetrain.drive_for(FORWARD, 24, INCHES)
wait(0.5, SECONDS)
print('mark B')
drivetrain.turn_for(RIGHT, 90, DEGREES)
wait(0.5, SECONDS)
print('mark C')
";
Protobot.RobotCode.CodeStore.Set(code);
Protobot.Motion.Sandbox.Stop(); Protobot.Motion.Sandbox.Begin();
var rb = Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb;
var marks = new System.Collections.Generic.List<string>();
Protobot.RobotCode.RobotProgram.Printed += s => { if (s.StartsWith("mark")) marks.Add(s.Trim() + " z=" + rb.position.z.ToString("0.00") + " x=" + rb.position.x.ToString("0.00") + " yaw=" + rb.rotation.eulerAngles.y.ToString("0.0")); UnityEngine.PlayerPrefs.SetString("t_code_marks", string.Join(" | ", marks)); };
string err; int line;
var ok = Protobot.RobotCode.RobotProgram.Start(code, Protobot.RobotCode.RobotProgram.Period.Program, 0, out err, out line);
return "started " + ok + " " + err;
