{config}
# Four points, each with its own heading: stop 2 s at each and write down where the robot is vs the goal, then back to
# (0, 0) facing 0. Each goal is reached along its own heading: first a spot 14" short of it, lined up; there it aims
# at the goal; then straight onto the goal, so the last turn is tiny (a turn in place shifts a robot a little: it never turns
# exactly about its tracking center). run.sh adds the truth to each "mark" line.
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

LEAD_IN = 14   # inches: how far out the robot lines up with the goal's heading

goals = [("P1", 24, 24, 90), ("P2", 48, 0, 180), ("P3", 24, -24, -90), ("P4", -12, 12, 45), ("HOME", 0, 0, 0)]


def angle_diff(a, b):
    return (a - b + 180) % 360 - 180


def drive_to(x, y):
    p = chassis.get_pose()
    if math.hypot(x - p.x, y - p.y) < 0.3:
        return
    chassis.turn_to_point(x, y, 2000, wait=True)
    chassis.move_to_point(x, y, 4000, wait=True)
    chassis.move_to_point(x, y, 1500, max_speed=40, wait=True)   # slow final approach


def go(x, y, h):
    # the lead-in spot: LEAD_IN inches back from the goal along its heading (heading clockwise from +y)
    sx = x - LEAD_IN * math.sin(math.radians(h))
    sy = y - LEAD_IN * math.cos(math.radians(h))
    drive_to(sx, sy)
    tries = 0
    ax, ay = x, y   # where it aims: the goal, moved against the last try's miss (a robot misses the same way each time)
    for attempt in range(3):
        tries = attempt + 1
        # aim (move_to_point stops steering in its last 7.5", like LemLib's: the aim must be right)
        chassis.turn_to_point(ax, ay, 2000, wait=True)
        chassis.move_to_point(ax, ay, 3000, wait=True)
        chassis.move_to_point(ax, ay, 1500, max_speed=40, wait=True)
        chassis.turn_to_heading(h, 1500, wait=True)
        p = chassis.get_pose()
        if math.hypot(p.x - x, p.y - y) <= 0.3 or attempt == 2:
            break
        ax -= p.x - x
        ay -= p.y - y
        # still off: back straight up to the lead-in spot (it's behind, along the heading: hardly any turn) and again
        chassis.move_to_point(sx, sy, 3000, forwards=False, wait=True)
    return tries


for name, x, y, h in goals:
    tries = go(x, y, h)
    p = chassis.get_pose()
    print("mark", name, "goal x=%.2f y=%.2f h=%.1f  odom x=%.2f y=%.2f h=%.2f  off %.2f in %.2f deg  (%d tries)"
          % (x, y, h, p.x, p.y, p.theta, math.hypot(p.x - x, p.y - y), angle_diff(p.theta, h), tries))
    wait(2, SECONDS)
print("mark", "END")
