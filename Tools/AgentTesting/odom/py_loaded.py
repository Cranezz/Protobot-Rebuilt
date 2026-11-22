# Odometry and autonomous motions, the way LemLib / EZ-Template teams write them
# (same names as LemLib). x, y in inches; heading in degrees, clockwise from forward.
from vex import *
from motion import *

# ---- Robot configuration (code window: Insert devices) ----
from vex import *

brain = Brain()
controller = Controller()

# Motors: port, cartridge (red 36:1 100 rpm, green 18:1 200 rpm, blue 6:1 600 rpm), reversed
right_back = Motor(Ports.PORT1, GearSetting.RATIO_6_1, True)
right_middle = Motor(Ports.PORT2, GearSetting.RATIO_6_1, True)
right_front = Motor(Ports.PORT3, GearSetting.RATIO_6_1, True)
left_back = Motor(Ports.PORT4, GearSetting.RATIO_6_1, False)
left_middle = Motor(Ports.PORT5, GearSetting.RATIO_6_1, False)
left_front = Motor(Ports.PORT6, GearSetting.RATIO_6_1, False)

# Sensors
inertial = Inertial(Ports.PORT7)
horizontal_tracking = Rotation(Ports.PORT8)
vertical_tracking = Rotation(Ports.PORT9)

# Drivetrain (measured from the build: 3.25" wheels, track width 14.125", 1.667 motor turns per wheel turn)
left_drive = MotorGroup(left_back, left_middle, left_front)
right_drive = MotorGroup(right_back, right_middle, right_front)
drivetrain = SmartDrive(left_drive, right_drive, inertial, 10.21, 14.125, 14.125, INCHES, 1.667)
# ---- End of robot configuration ----

# Track width (in), wheel diameter (in), wheel rpm, horizontal drift (2 with omnis all round, 8 with traction wheels)
odom_drive = Drivetrain(left_drive, right_drive, 14.125, 3.25, 360, 2)
# kP, kI, kD, anti windup, small error, small error timeout (ms), large error, large error timeout (ms), slew
lateral = ControllerSettings(10, 0, 3, 3, 1, 100, 3, 500, 20)
angular = ControllerSettings(2, 0, 10, 3, 1, 100, 3, 500, 0)
# Tracking wheels (measured from the robot: wheel size, offset from the turning center; reversed so
# forward and right count up)
vertical_wheel = TrackingWheel(Rotation(Ports.PORT9, False), 2.75, -1.23)   # rolls forward, 1.23" left of the turning center
horizontal_wheel = TrackingWheel(Rotation(Ports.PORT8, False), 2.75, -3.77)   # rolls sideways, 3.77" behind the turning center
sensors = OdomSensors(vertical_wheel, None, horizontal_wheel, None, inertial)
chassis = Chassis(odom_drive, lateral, angular, sensors)

chassis.calibrate()           # the Inertial sensor calibrates (about 2 s)
chassis.set_pose(0, 0, 0)

# Motions run in the background: each waits for the one before. wait=True waits for it to finish.
chassis.move_to_point(0, 24, 2000)
chassis.turn_to_heading(90, 1000)
chassis.move_to_pose(24, 36, 0, 3000)       # boomerang: arrives at (24, 36) facing forward
chassis.wait_until_done()
print("mark pose:", chassis.get_pose())

# Pure pursuit along a path of points, then back to the start backwards
chassis.follow([(24, 36), (12, 48), (0, 48)], 10, 4000)
chassis.move_to_point(0, 0, 4000, forwards=False, wait=True)
print("mark back at", chassis.get_pose())
