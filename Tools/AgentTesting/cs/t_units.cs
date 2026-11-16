var code = @"#include ""main.h""
void initialize() { printf(""%.3f %.1f %.0f %.1f %.1f %d"", 12_cm, 1_ft, 2_s, 3.14159_rad, 1_tile, 7 / 2); }
void autonomous() {}
void opcontrol() {}
";
var log = new System.Text.StringBuilder();
Protobot.Motion.Sandbox.Stop(); Protobot.Motion.Sandbox.Begin();
Protobot.RobotCode.RobotProgram.Printed += s => log.Append(s);
string err; int line;
Protobot.RobotCode.RobotProgram.Start(code, Protobot.RobotCode.RobotProgram.Period.Autonomous, 2, out err, out line);
for (var i = 0; i < 20; i++) Protobot.RobotCode.RobotProgram.Step(0.01f);
Protobot.RobotCode.RobotProgram.Stop(null); Protobot.Motion.Sandbox.Stop();
return err + "|" + log;
