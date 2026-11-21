// period: autonomous
{cppconfig}
#include "lemlib/api.hpp"
// Odometry with two tracking wheels and the IMU, as LemLib's example sets it up
pros::Rotation vertical_sensor(-{vert});
pros::Rotation horizontal_sensor(-{horiz});
pros::Imu inertial({imu});
lemlib::TrackingWheel vertical_wheel(&vertical_sensor, lemlib::Omniwheel::NEW_275, {voff});
lemlib::TrackingWheel horizontal_wheel(&horizontal_sensor, lemlib::Omniwheel::NEW_275, {hoff});
lemlib::Drivetrain drivetrain(&left_drive, &right_drive, 14.125, lemlib::Omniwheel::NEW_325, 360, 2);
lemlib::ControllerSettings lateral(10, 0, 3, 3, 1, 100, 3, 500, 20);
lemlib::ControllerSettings angular(2, 0, 10, 3, 1, 100, 3, 500, 0);
lemlib::OdomSensors sensors(&vertical_wheel, nullptr, &horizontal_wheel, nullptr, &inertial);
lemlib::Chassis chassis(drivetrain, lateral, angular, sensors);

void mark(const char* name) {
    lemlib::Pose p = chassis.getPose();
    printf("mark %s odom x=%.2f y=%.2f h=%.2f\n", name, p.x, p.y, p.theta);
}

void initialize() {
    chassis.calibrate();
}

void autonomous() {
    chassis.setPose(0, 0, 0);
    chassis.moveToPoint(0, 24, 3000);
    chassis.waitUntilDone();
    mark("A");
    chassis.turnToHeading(90, 2000);
    chassis.waitUntilDone();
    mark("B");
    chassis.moveToPose(24, 24, 90, 4000);
    chassis.waitUntilDone();
    mark("C");
    chassis.turnToHeading(225, 2000);
    chassis.waitUntilDone();
    mark("D");
    chassis.moveToPoint(0, 0, 4000);
    chassis.waitUntilDone();
    mark("E");
    chassis.turnToHeading(0, 2000);
    chassis.waitUntilDone();
    mark("F");
}

void opcontrol() {}
