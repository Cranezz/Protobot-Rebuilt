var code = "from vex import *\nleft = MotorGroup(Motor(Ports.PORT4), Motor(Ports.PORT5), Motor(Ports.PORT6))\nright = MotorGroup(Motor(Ports.PORT1, True), Motor(Ports.PORT2, True), Motor(Ports.PORT3, True))\nleft.spin(FORWARD, 50, PERCENT)\nright.spin(FORWARD, 50, PERCENT)\n";
Protobot.Motion.Sandbox.Stop(); Protobot.Motion.Sandbox.Begin();
string err; int line;
var ok = Protobot.RobotCode.RobotProgram.Start(code, Protobot.RobotCode.RobotProgram.Period.Program, 0, out err, out line);
return "started " + ok;
