// A VEXcode C++ competition program (VEXcode's template + robot-config style) on the 6-motor drive.
var code = @"#include ""vex.h""
using namespace vex;

// robot-config
brain Brain;
controller Controller1 = controller(primary);
motor LeftFront = motor(PORT6, ratio6_1, false);
motor LeftMiddle = motor(PORT5, ratio6_1, false);
motor LeftBack = motor(PORT4, ratio6_1, false);
motor RightFront = motor(PORT3, ratio6_1, true);
motor RightMiddle = motor(PORT2, ratio6_1, true);
motor RightBack = motor(PORT1, ratio6_1, true);
motor_group LeftDrive = motor_group(LeftFront, LeftMiddle, LeftBack);
motor_group RightDrive = motor_group(RightFront, RightMiddle, RightBack);
drivetrain Drivetrain = drivetrain(LeftDrive, RightDrive, 10.21, 14.125, 14.125, inches, 1.667);

competition Competition;
int autonCount = 0;

void pre_auton(void) {
  Brain.Screen.clearScreen();
  Brain.Screen.print(""Ready %d"", 1);
}

void autonomous(void) {
  autonCount++;
  Drivetrain.setDriveVelocity(50, percent);
  Drivetrain.driveFor(forward, 24, inches);
  printf(""mark A %d\n"", autonCount);
  Drivetrain.turnFor(right, 90, degrees);
  printf(""mark B\n"");
  LeftDrive.spinFor(forward, 360, degrees, false);
  RightDrive.spinFor(forward, 360, degrees);
  printf(""mark C %.1f\n"", LeftDrive.position(degrees));
  Brain.Screen.newLine();
  Brain.Screen.print(""Auton done at %.1f s"", Brain.Timer.time(sec));
}

void usercontrol(void) {
  while (1) {
    int fwd = Controller1.Axis3.position(percent);
    int turn = Controller1.Axis1.position(percent);
    LeftDrive.spin(forward, fwd + turn, percent);
    RightDrive.spin(forward, fwd - turn, percent);
    wait(20, msec);
  }
}

int main() {
  Competition.autonomous(autonomous);
  Competition.drivercontrol(usercontrol);
  pre_auton();
  while (1) {
    wait(100, msec);
  }
}
";
Protobot.RobotCode.CodeStore.Set(code);
Protobot.Motion.Sandbox.Stop(); Protobot.Motion.Sandbox.Begin();
var rb = Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb;
var start = rb.position;
var log = new System.Text.StringBuilder();
UnityEngine.PlayerPrefs.SetString("t_code_marks", "");
Protobot.RobotCode.RobotProgram.Printed += s => { log.Append(s.StartsWith("mark") && rb != null ? s.TrimEnd() + "  TRUE x=" + (rb.position - start).x.ToString("0.0") + " y=" + (rb.position - start).z.ToString("0.0") + " yaw=" + rb.rotation.eulerAngles.y.ToString("0.0") + "\n" : s); UnityEngine.PlayerPrefs.SetString("t_code_marks", log.ToString()); };
string err; int line;
var ok = Protobot.RobotCode.RobotProgram.Start(code, Protobot.RobotCode.RobotProgram.Period.Autonomous, 15, out err, out line);
return "started " + ok + " " + err;
