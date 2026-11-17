#include "vex.h"

using namespace vex;

competition Competition;
motor LeftFront(PORT1, ratio6_1, true);
motor LeftBack(PORT2, ratio6_1, true);
motor RightFront(PORT3, ratio6_1, false);
motor RightBack(PORT4, ratio6_1, false);
motor_group LeftDrive(LeftFront, LeftBack);
motor_group RightDrive(RightFront, RightBack);
inertial Gyro(PORT10);
smartdrive Drive(LeftDrive, RightDrive, Gyro, 259.34, 320, 40, mm, 1);
motor Intake(PORT8, ratio18_1, false);
digital_out Clamp(Brain.ThreeWirePort.A);
controller Controller1(primary);

void toggleClamp() { Clamp.set(!Clamp.value()); }

void pre_auton(void) {
  Gyro.calibrate();
  while (Gyro.isCalibrating()) wait(20, msec);
}

void autonomous(void) {
  Drive.setDriveVelocity(60, percent);
  Drive.driveFor(forward, 24, inches);
  Drive.turnToHeading(90, degrees);
  Intake.spinFor(forward, 2, turns);
}

void usercontrol(void) {
  Controller1.ButtonL1.pressed(toggleClamp);
  while (1) {
    LeftDrive.spin(forward, Controller1.Axis3.position() + Controller1.Axis1.position(), percent);
    RightDrive.spin(forward, Controller1.Axis3.position() - Controller1.Axis1.position(), percent);
    if (Controller1.ButtonR1.pressing()) Intake.spin(forward, 100, percent);
    else Intake.stop(brake);
    Brain.Screen.clearScreen();
    Brain.Screen.printAt(10, 40, "Heading %.1f", Gyro.heading(degrees));
    Controller1.Screen.setCursor(1, 1);
    Controller1.Screen.print("%d", (int)Intake.velocity(rpm));
    wait(20, msec);
  }
}

int main() {
  Competition.autonomous(autonomous);
  Competition.drivercontrol(usercontrol);
  pre_auton();
  while (1) wait(100, msec);
}
