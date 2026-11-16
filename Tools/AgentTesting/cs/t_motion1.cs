var code = Protobot.RobotCode.DeviceConfig.Generate() + @"
from motion import *
chassis_drive = Drivetrain(left_drive, right_drive, 14.125, 3.25, 360, 2)
lateral = ControllerSettings(10, 0, 3, 3, 1, 100, 3, 500, 20)
angular = ControllerSettings(2, 0, 10, 3, 1, 100, 3, 500, 0)
chassis = Chassis(chassis_drive, lateral, angular, OdomSensors(None, None, None, None, None))
chassis.calibrate()
chassis.set_pose(0, 0, 0)
chassis.move_to_point(0, 24, 3000, wait=True)
print('mark A', chassis.get_pose())
chassis.turn_to_heading(90, 2000, wait=True)
print('mark B', chassis.get_pose())
chassis.move_to_pose(24, 24, 90, 4000, wait=True)
print('mark C', chassis.get_pose())
chassis.turn_to_heading(0, 2000, wait=True)
chassis.follow([(24, 24), (30, 36), (24, 48), (12, 54)], 10, 6000, wait=True)
print('mark D', chassis.get_pose())
";
Protobot.RobotCode.CodeStore.Set(code);
Protobot.Motion.Sandbox.Stop(); Protobot.Motion.Sandbox.Begin();
var rb = Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb;
var start = rb.position;
var marks = new System.Collections.Generic.List<string>();
UnityEngine.PlayerPrefs.SetString("t_code_marks", "");
Protobot.RobotCode.RobotProgram.Printed += s => { if (s.StartsWith("mark") && rb != null) { var d = rb.position - start; marks.Add(s.Trim() + " TRUE x=" + d.x.ToString("0.0") + " y=" + d.z.ToString("0.0") + " yaw=" + rb.rotation.eulerAngles.y.ToString("0.0")); UnityEngine.PlayerPrefs.SetString("t_code_marks", string.Join(" | ", marks)); } };
string err; int line;
var ok = Protobot.RobotCode.RobotProgram.Start(code, Protobot.RobotCode.RobotProgram.Period.Program, 0, out err, out line);
return "started " + ok + " " + err;
