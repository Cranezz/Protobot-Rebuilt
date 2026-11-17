#include "main.h"

pros::Controller master(pros::E_CONTROLLER_MASTER);
pros::MotorGroup left_mg({1, -2, 3});
pros::MotorGroup right_mg({-4, 5, -6});
pros::Motor intake(8, pros::MotorGearset::blue);
pros::adi::DigitalOut clamp('A');
pros::Imu imu(10);
pros::Rotation lift_sensor(11);
pros::Distance dist(12);
pros::Optical color(13);
bool clamped = false;

void initialize() {
  pros::lcd::initialize();
  imu.reset();
  while (imu.is_calibrating()) pros::delay(10);
  intake.set_brake_mode(pros::E_MOTOR_BRAKE_HOLD);
}

void autonomous() {
  left_mg.move_relative(720, 100);
  right_mg.move_relative(720, 100);
  pros::delay(1500);
  intake.move_velocity(600);
  pros::delay(500);
  intake.brake();
}

void opcontrol() {
  while (true) {
    int dir = master.get_analog(ANALOG_LEFT_Y);
    int turn = master.get_analog(ANALOG_RIGHT_X);
    left_mg.move(dir + turn);
    right_mg.move(dir - turn);
    if (master.get_digital(DIGITAL_R1)) intake.move(127);
    else if (master.get_digital(DIGITAL_R2)) intake.move(-127);
    else intake.move(0);
    if (master.get_digital_new_press(DIGITAL_L1)) {
      clamped = !clamped;
      clamp.set_value(clamped);
    }
    pros::lcd::print(0, "heading %.1f  lift %d", imu.get_heading(), lift_sensor.get_angle());
    pros::lcd::print(1, "dist %d mm hue %.0f", dist.get(), color.get_hue());
    pros::delay(20);
  }
}
