// An EZ-Template program as its template project is written (main.cpp + autons.cpp), on the 6-motor drive.
var code = @"#include ""main.h""

// EZ-Template 2.x
Drive chassis(
  {4, 5, 6}, {-1, -2, -3},
  7,       // IMU port
  3.25,    // wheel diameter
  600,     // cartridge rpm
  1.667);  // external ratio (driven / driving)

void default_constants() {
  chassis.set_slew_min_power(80, 80);
  chassis.set_slew_distance(7, 7);
  chassis.set_pid_constants(&chassis.headingPID, 11, 0, 20, 0);
  chassis.set_pid_constants(&chassis.forward_drivePID, 0.45, 0, 5, 0);
  chassis.set_pid_constants(&chassis.backward_drivePID, 0.45, 0, 5, 0);
  chassis.set_pid_constants(&chassis.turnPID, 5, 0.003, 35, 15);
  chassis.set_pid_constants(&chassis.swingPID, 7, 0, 45, 0);
}

void exit_condition_defaults() {
  chassis.set_exit_condition(chassis.turn_exit, 100, 3, 500, 7, 500, 500);
  chassis.set_exit_condition(chassis.swing_exit, 100, 3, 500, 7, 500, 500);
  chassis.set_exit_condition(chassis.drive_exit, 80, 50, 300, 150, 500, 500);
}

void mark(const char* name) {
  printf(""mark %s gyro %.1f\n"", name, chassis.get_gyro());
}

void test_auton() {
  chassis.set_drive_pid(24, 110, true);
  chassis.wait_drive();
  mark(""A"");
  chassis.set_turn_pid(90, 90);
  chassis.wait_drive();
  mark(""B"");
  chassis.set_swing_pid(ez::LEFT_SWING, 0, 110);
  chassis.wait_drive();
  mark(""C"");
  chassis.set_drive_pid(-24, 110);
  chassis.wait_until(-12);
  mark(""D"");
  chassis.wait_drive();
  mark(""E"");
}

void initialize() {
  ez::print_ez_template();
  default_constants();
  exit_condition_defaults();
  ez::as::auton_selector.add_autons({
    Auton(""Test\n\nv2 names"", test_auton),
  });
  chassis.initialize();
  ez::as::init_auton_selector();
}

void autonomous() {
  chassis.reset_pid_targets();
  chassis.reset_gyro();
  chassis.reset_drive_sensor();
  chassis.set_drive_brake(MOTOR_BRAKE_HOLD);
  ez::as::auton_selector.call_selected_auton();
}

void opcontrol() {
  chassis.set_drive_brake(MOTOR_BRAKE_COAST);
  while (1) {
    chassis.tank();
    pros::delay(ez::util::DELAY_TIME);
  }
}
";
UnityEngine.PlayerPrefs.SetInt("V2.Code.EzAuton", 0);
Protobot.RobotCode.CodeStore.Set(code);
Protobot.Motion.Sandbox.Stop(); Protobot.Motion.Sandbox.Begin();
var rb = Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb;
var start = rb.position;
var log = new System.Text.StringBuilder();
UnityEngine.PlayerPrefs.SetString("t_code_marks", "");
Protobot.RobotCode.RobotProgram.Printed += s => { log.Append(s.StartsWith("mark") && rb != null ? s.TrimEnd() + "  TRUE x=" + (rb.position - start).x.ToString("0.0") + " y=" + (rb.position - start).z.ToString("0.0") + " yaw=" + rb.rotation.eulerAngles.y.ToString("0.0") + " t=" + Protobot.RobotCode.RobotProgram.Current.Time.ToString("0.0") + "\n" : s); UnityEngine.PlayerPrefs.SetString("t_code_marks", log.ToString()); };
string err; int line;
var ok = Protobot.RobotCode.RobotProgram.Start(code, Protobot.RobotCode.RobotProgram.Period.Autonomous, 15, out err, out line);
return "started " + ok + " " + err;
