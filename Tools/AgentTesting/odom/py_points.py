{config}
# Four points, each with its own heading: stop 2 s at each, write down where the robot is vs the goal, then back to
# (0, 0) facing 0. run.sh adds the truth to each "mark" line.
from motion import *
import math
vert = Rotation(Ports.PORT{vert}, True)
horiz = Rotation(Ports.PORT{horiz}, True)
gyro = Inertial(Ports.PORT{imu})
odom_drive = Drivetrain(left_drive, right_drive, 14.125, Omniwheel.NEW_325, 360, 2)
# tight exits so each stop settles on the goal: within 0.25" for 250 ms, within 0.5 deg for 250 ms
lateral = ControllerSettings(10, 0, 3, 3, 0.25, 250, 1, 1000, 20)
angular = ControllerSettings(2, 0, 10, 3, 0.5, 250, 1, 1000, 0)
vertical_wheel = TrackingWheel(vert, Omniwheel.NEW_275, {voffc})
horizontal_wheel = TrackingWheel(horiz, Omniwheel.NEW_275, {hoffc})
sensors = OdomSensors(vertical_wheel, None, horizontal_wheel, None, gyro)
chassis = Chassis(odom_drive, lateral, angular, sensors)
chassis.calibrate()
chassis.set_pose(0, 0, 0)

goals = [("P1", 24, 24, 90), ("P2", 48, 0, 180), ("P3", 24, -24, -90), ("P4", -12, 12, 45), ("HOME", 0, 0, 0)]


def angle_diff(a, b):
    return (a - b + 180) % 360 - 180


def go(x, y, h):
    p = chassis.get_pose()
    # face the point, drive to it, then turn to the heading (each settles before the next)
    if math.hypot(x - p.x, y - p.y) > 1:
        chassis.turn_to_point(x, y, 2000, wait=True)
        chassis.move_to_point(x, y, 4000, wait=True)
        chassis.move_to_point(x, y, 1500, max_speed=40, wait=True)   # slow final approach
    q = chassis.get_pose()
    print("mark", "moved", "odom x=%.2f y=%.2f h=%.2f  off %.2f in" % (q.x, q.y, q.theta, math.hypot(q.x - x, q.y - y)))
    chassis.turn_to_heading(h, 2000, wait=True)


for name, x, y, h in goals:
    go(x, y, h)
    p = chassis.get_pose()
    print("mark", name, "goal x=%.2f y=%.2f h=%.1f  odom x=%.2f y=%.2f h=%.2f  off %.2f in %.2f deg"
          % (x, y, h, p.x, p.y, p.theta, math.hypot(p.x - x, p.y - y), angle_diff(p.theta, h)))
    wait(2, SECONDS)
