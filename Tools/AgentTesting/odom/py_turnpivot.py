{config}
# Turns in place and marks after each turn: run.sh's truth is the build's origin, so the point the robot really turns
# about can be solved from where the origin went (c = (I - R)^-1 (p' - R p)).
from motion import *
vert = Rotation(Ports.PORT{vert}, True)
horiz = Rotation(Ports.PORT{horiz}, True)
gyro = Inertial(Ports.PORT{imu})
odom_drive = Drivetrain(left_drive, right_drive, 14.125, Omniwheel.NEW_325, 360, 2)
lateral = ControllerSettings(10, 0, 3, 3, 0.25, 250, 1, 1000, 20)
angular = ControllerSettings(2, 0, 10, 3, 0.5, 250, 1, 1000, 0)
vertical_wheel = TrackingWheel(vert, Omniwheel.NEW_275, {voff})
horizontal_wheel = TrackingWheel(horiz, Omniwheel.NEW_275, {hoff})
sensors = OdomSensors(vertical_wheel, None, horizontal_wheel, None, gyro)
chassis = Chassis(odom_drive, lateral, angular, sensors)
chassis.calibrate()
chassis.set_pose(0, 0, 0)


def mark(name):
    p = chassis.get_pose()
    print("mark", name, "odom x=%.2f y=%.2f h=%.2f" % (p.x, p.y, p.theta))


mark("start")
for h in [90, 180, 270, 0, -90]:
    chassis.turn_to_heading(h, 3000, wait=True)
    wait(500, MSEC)
    mark("turn%d" % h)
print("mark", "END")
