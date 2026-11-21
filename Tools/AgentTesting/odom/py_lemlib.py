{config}
# Odometry with two tracking wheels and the Inertial sensor (the motion module, LemLib's names)
from motion import *
vert = Rotation(Ports.PORT{vert}, True)
horiz = Rotation(Ports.PORT{horiz}, True)
gyro = Inertial(Ports.PORT{imu})
odom_drive = Drivetrain(left_drive, right_drive, 14.125, Omniwheel.NEW_325, 360, 2)
lateral = ControllerSettings(10, 0, 3, 3, 1, 100, 3, 500, 20)
angular = ControllerSettings(2, 0, 10, 3, 1, 100, 3, 500, 0)
vertical_wheel = TrackingWheel(vert, Omniwheel.NEW_275, {voff})
horizontal_wheel = TrackingWheel(horiz, Omniwheel.NEW_275, {hoff})
sensors = OdomSensors(vertical_wheel, None, horizontal_wheel, None, gyro)
chassis = Chassis(odom_drive, lateral, angular, sensors)
chassis.calibrate()
chassis.set_pose(0, 0, 0)


def mark(name):
    p = chassis.get_pose()
    print("mark", name, "odom x=%.2f y=%.2f h=%.2f" % (p.x, p.y, p.theta))


chassis.move_to_point(0, 24, 3000, wait=True)
mark("A")
chassis.turn_to_heading(90, 2000, wait=True)
mark("B")
chassis.move_to_pose(24, 24, 90, 4000, wait=True)
mark("C")
chassis.turn_to_heading(0, 2000, wait=True)
mark("C2")
chassis.follow([(24, 24), (30, 36), (24, 48), (12, 54)], 10, 6000, wait=True)
mark("D")
chassis.move_to_point(0, 0, 5000, forwards=False, wait=True)
mark("E")
chassis.turn_to_heading(0, 2000, wait=True)
mark("F")
