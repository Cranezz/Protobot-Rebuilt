// An EZ-Template program as its template project is written (main.cpp + autons.cpp), on the 6-motor drive.
var code = @"#include ""main.h""

ez::Drive chassis(
    {4, 5, 6},     // Left Chassis Ports (negative port will reverse it!)
    {-1, -2, -3},  // Right Chassis Ports (negative port will reverse it!)
    7,      // IMU Port
    3.25,   // Wheel Diameter
    360);   // Wheel RPM = cartridge * (motor gear / wheel gear)

const int DRIVE_SPEED = 110;
const int TURN_SPEED = 90;
const int SWING_SPEED = 110;

void default_constants() {
  chassis.pid_drive_constants_set(20.0, 0.0, 100.0);
  chassis.pid_heading_constants_set(11.0, 0.0, 20.0);
  chassis.pid_turn_constants_set(3.0, 0.05, 20.0, 15.0);
  chassis.pid_swing_constants_set(6.0, 0.0, 65.0);
  chassis.pid_odom_angular_constants_set(6.5, 0.0, 52.5);
  chassis.pid_odom_boomerang_constants_set(5.8, 0.0, 32.5);
  chassis.pid_turn_exit_condition_set(90_ms, 3_deg, 250_ms, 7_deg, 500_ms, 500_ms);
  chassis.pid_swing_exit_condition_set(90_ms, 3_deg, 250_ms, 7_deg, 500_ms, 500_ms);
  chassis.pid_drive_exit_condition_set(90_ms, 1_in, 250_ms, 3_in, 500_ms, 500_ms);
  chassis.pid_odom_turn_exit_condition_set(90_ms, 3_deg, 250_ms, 7_deg, 500_ms, 750_ms);
  chassis.pid_odom_drive_exit_condition_set(90_ms, 1_in, 250_ms, 3_in, 500_ms, 750_ms);
  chassis.pid_turn_chain_constant_set(3_deg);
  chassis.pid_swing_chain_constant_set(5_deg);
  chassis.pid_drive_chain_constant_set(3_in);
  chassis.slew_turn_constants_set(3_deg, 70);
  chassis.slew_drive_constants_set(3_in, 70);
  chassis.slew_swing_constants_set(3_in, 80);
  chassis.odom_turn_bias_set(0.9);
  chassis.odom_look_ahead_set(7_in);
  chassis.odom_boomerang_distance_set(16_in);
  chassis.odom_boomerang_dlead_set(0.625);
  chassis.pid_angle_behavior_set(ez::shortest);
}

void mark(const char* name) {
  printf(""mark %s %.1f %.1f %.1f\n"", name, chassis.odom_x_get(), chassis.odom_y_get(), chassis.odom_theta_get());
}

void test_auton() {
  chassis.pid_drive_set(24_in, DRIVE_SPEED, true);
  chassis.pid_wait();
  mark(""A"");
  chassis.pid_turn_set(90_deg, TURN_SPEED);
  chassis.pid_wait();
  mark(""B"");
  chassis.pid_drive_set(-12_in, DRIVE_SPEED);
  chassis.pid_wait();
  mark(""C"");
  chassis.pid_swing_set(ez::LEFT_SWING, 0_deg, SWING_SPEED, 0);
  chassis.pid_wait();
  mark(""D"");
  chassis.pid_odom_set({{0_in, 24_in}, fwd, DRIVE_SPEED}, true);
  chassis.pid_wait();
  mark(""E"");
}

void other_auton() { printf(""wrong auton\n""); }

void initialize() {
  ez::ez_template_print();
  pros::delay(500);
  chassis.opcontrol_curve_buttons_toggle(true);
  chassis.opcontrol_drive_activebrake_set(0.0);
  chassis.opcontrol_curve_default_set(0.0, 0.0);
  default_constants();
  ez::as::auton_selector.autons_add({
      {""Test\n\nDrive, turn, swing, odom"", test_auton},
      {""Other"", other_auton},
  });
  chassis.initialize();
  ez::as::initialize();
  master.rumble(chassis.drive_imu_calibrated() ? ""."" : ""---"");
}

void disabled() {}
void competition_initialize() {}

void autonomous() {
  chassis.pid_targets_reset();
  chassis.drive_imu_reset();
  chassis.drive_sensor_reset();
  chassis.odom_xyt_set(0_in, 0_in, 0_deg);
  chassis.drive_brake_set(MOTOR_BRAKE_HOLD);
  ez::as::auton_selector.selected_auton_call();
}

void ez_template_extras() {
  if (!pros::competition::is_connected()) {
    if (master.get_digital_new_press(DIGITAL_X))
      chassis.pid_tuner_toggle();
    if (master.get_digital(DIGITAL_B) && master.get_digital(DIGITAL_DOWN)) {
      pros::motor_brake_mode_e_t preference = chassis.drive_brake_get();
      autonomous();
      chassis.drive_brake_set(preference);
    }
    chassis.pid_tuner_iterate();
  } else {
    if (chassis.pid_tuner_enabled())
      chassis.pid_tuner_disable();
  }
}

void opcontrol() {
  chassis.drive_brake_set(MOTOR_BRAKE_COAST);
  while (1) {
    ez_template_extras();
    chassis.opcontrol_tank();
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
