// period: autonomous
#include "main.h"
// EZ-Template with two tracking wheels (left and back) and the IMU
ez::Drive chassis({left_ports}, {right_ports}, {imu}, 3.25, 360);
ez::tracking_wheel vert_tracker(-{vert}, 2.75, {voffabs});
ez::tracking_wheel horiz_tracker(-{horiz}, 2.75, {hoffabs});

void mark(const char* name) {
    printf("mark %s odom x=%.2f y=%.2f h=%.2f\n", name, chassis.odom_x_get(), chassis.odom_y_get(), chassis.odom_theta_get());
}

void initialize() {
    chassis.odom_tracker_left_set(&vert_tracker);
    chassis.odom_tracker_back_set(&horiz_tracker);
    chassis.pid_drive_constants_set(20, 0, 100);
    chassis.pid_heading_constants_set(11, 0, 20);
    chassis.pid_turn_constants_set(3, 0.05, 20, 15);
    chassis.pid_odom_angular_constants_set(6.5, 0.0, 52.5);
    chassis.pid_odom_boomerang_constants_set(5.8, 0.0, 32.5);
    chassis.initialize();
}

void autonomous() {
    chassis.pid_targets_reset();
    chassis.drive_imu_reset();
    chassis.drive_sensor_reset();
    chassis.odom_xyt_set(0_in, 0_in, 0_deg);
    chassis.pid_odom_set({{0_in, 24_in}, fwd, 110});
    chassis.pid_wait();
    mark("A");
    chassis.pid_turn_set(90_deg, 90);
    chassis.pid_wait();
    mark("B");
    chassis.pid_odom_set({{24_in, 24_in, 90_deg}, fwd, 110});
    chassis.pid_wait();
    mark("C");
    chassis.pid_odom_set({{{24_in, 24_in}, fwd, 110}, {{30_in, 36_in}, fwd, 110}, {{24_in, 48_in}, fwd, 110}, {{12_in, 54_in}, fwd, 110}});
    chassis.pid_wait();
    mark("D");
    chassis.pid_odom_set({{0_in, 0_in}, rev, 110});
    chassis.pid_wait();
    mark("E");
    chassis.pid_turn_set(0_deg, 90);
    chassis.pid_wait();
    mark("F");
}

void opcontrol() {}
