// Fills the "Odometry: 4 points and back" example from the loaded robot (as the Examples menu does) and saves it as
// odom/ex_points.py for run.sh, with its result lines as marks, an END mark, and the truth measured at the turning center
var ex = Protobot.RobotCode.Examples.All.First(e => e.name.StartsWith("Odometry: 4 points"));
var code = Protobot.RobotCode.Examples.Fill(ex.code);
code = code.Replace("print(\"%-4s goal", "print(\"mark\", \"%-4s goal").Replace("print(\"done\")", "print(\"mark\", \"END\")");
System.IO.File.WriteAllText("Tools/AgentTesting/odom/ex_points.py", "# truth: center\n" + code);
var lines = code.Split('\n');
return string.Join("\n", lines.Where(l => l.Contains("TrackingWheel(") || l.Contains("OdomSensors(") || l.Contains("Drivetrain(") || l.Contains("MotorGroup(")).ToArray());
