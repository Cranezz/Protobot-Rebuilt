namespace Protobot.RobotCode {
    /// <summary>V2 (Phase 9): starting programs for the code panel's Examples menu (VEXcode V5 Python style).</summary>
    public static class Examples {
        public static readonly (string name, string description, string code)[] All = {
            ("Competition template", "Autonomous and driver control, like VEXcode's template", Competition),
            ("Tank drive", "Each stick drives one side", Tank),
            ("Arcade drive", "Left stick forward/back, right stick turns", Arcade),
            ("Autonomous square", "Drives a square with the motors' own position control", Square),
            ("Gyro turns (SmartDrive)", "Turns to exact headings with the Inertial sensor", Gyro),
            ("Pneumatics on a button", "Button A toggles a piston", Pneumatics),
            ("Read the sensors", "Prints every sensor to the Brain's screen", Sensors),
            ("PID drive", "Your own PID loop to drive a distance", Pid),
        };

        private const string Competition = @"# Competition template (VEXcode V5 Python style)
from vex import *

brain = Brain()
controller = Controller(PRIMARY)

# Set the ports to the ones in Edit > Devices.
left_motor = Motor(Ports.PORT1, GearSetting.RATIO_18_1, False)
right_motor = Motor(Ports.PORT10, GearSetting.RATIO_18_1, True)


def pre_autonomous():
    # Runs once when the program starts (calibrate sensors here).
    brain.screen.clear_screen()
    brain.screen.print(""Ready"")


def autonomous():
    # The 15 second autonomous period.
    left_motor.spin_for(FORWARD, 720, DEGREES, wait=False)
    right_motor.spin_for(FORWARD, 720, DEGREES)


def user_control():
    # Driver control: tank drive.
    while True:
        left_motor.spin(FORWARD, controller.axis3.position(), PERCENT)
        right_motor.spin(FORWARD, controller.axis2.position(), PERCENT)
        wait(20, MSEC)


comp = Competition(user_control, autonomous)
pre_autonomous()
";

        private const string Tank = @"from vex import *

controller = Controller(PRIMARY)
left_motor = Motor(Ports.PORT1, GearSetting.RATIO_18_1, False)
right_motor = Motor(Ports.PORT10, GearSetting.RATIO_18_1, True)

while True:
    left_motor.spin(FORWARD, controller.axis3.position(), PERCENT)
    right_motor.spin(FORWARD, controller.axis2.position(), PERCENT)
    wait(20, MSEC)
";

        private const string Arcade = @"from vex import *

controller = Controller(PRIMARY)
left_drive = MotorGroup(Motor(Ports.PORT1), Motor(Ports.PORT2))
right_drive = MotorGroup(Motor(Ports.PORT9, True), Motor(Ports.PORT10, True))

while True:
    forward = controller.axis3.position()
    turn = controller.axis1.position()
    left_drive.spin(FORWARD, forward + turn, PERCENT)
    right_drive.spin(FORWARD, forward - turn, PERCENT)
    wait(20, MSEC)
";

        private const string Square = @"from vex import *

brain = Brain()
left_motor = Motor(Ports.PORT1, GearSetting.RATIO_18_1, False)
right_motor = Motor(Ports.PORT10, GearSetting.RATIO_18_1, True)
# wheel_travel: the wheel's circumference (4"" wheel: 12.57""); track_width: between the wheels.
drivetrain = DriveTrain(left_motor, right_motor, 12.57, 11, 10, INCHES, 1)
drivetrain.set_drive_velocity(50, PERCENT)
drivetrain.set_turn_velocity(30, PERCENT)

for side in range(4):
    drivetrain.drive_for(FORWARD, 24, INCHES)
    drivetrain.turn_for(RIGHT, 90, DEGREES)

print(""Done in"", round(brain.timer.time(SECONDS), 1), ""seconds"")
";

        private const string Gyro = @"from vex import *

brain = Brain()
left_motor = Motor(Ports.PORT1, GearSetting.RATIO_18_1, False)
right_motor = Motor(Ports.PORT10, GearSetting.RATIO_18_1, True)
imu = Inertial(Ports.PORT5)
drivetrain = SmartDrive(left_motor, right_motor, imu, 12.57, 11, 10, INCHES, 1)

imu.calibrate()
while imu.is_calibrating():
    wait(50, MSEC)

for heading in [90, 180, 270, 0]:
    drivetrain.drive_for(FORWARD, 18, INCHES)
    drivetrain.turn_to_heading(heading, DEGREES)
    print(""heading"", round(imu.heading(), 1))
";

        private const string Pneumatics = @"from vex import *

controller = Controller(PRIMARY)
piston = Pneumatics(Brain().three_wire_port.a)
extended = False


def toggle():
    global extended
    extended = not extended
    if extended:
        piston.open()
    else:
        piston.close()


controller.buttonA.pressed(toggle)
";

        private const string Sensors = @"from vex import *

brain = Brain()
distance = Distance(Ports.PORT4)
imu = Inertial(Ports.PORT5)
optical = Optical(Ports.PORT6)

while True:
    brain.screen.clear_screen()
    brain.screen.set_cursor(1, 1)
    brain.screen.print(""Distance:"", distance.object_distance(INCHES), ""in"")
    brain.screen.next_row()
    brain.screen.print(""Heading:"", imu.heading())
    brain.screen.next_row()
    brain.screen.print(""Hue:"", optical.hue())
    wait(100, MSEC)
";

        private const string Pid = @"from vex import *

left_motor = Motor(Ports.PORT1, GearSetting.RATIO_18_1, False)
right_motor = Motor(Ports.PORT10, GearSetting.RATIO_18_1, True)

WHEEL_CIRCUMFERENCE = 12.57   # inches (a 4"" wheel)


class PID:
    def __init__(self, kp, ki, kd):
        self.kp, self.ki, self.kd = kp, ki, kd
        self.total = 0
        self.last = 0

    def step(self, error, dt):
        self.total += error * dt
        change = (error - self.last) / dt
        self.last = error
        return self.kp * error + self.ki * self.total + self.kd * change


def drive_inches(distance):
    left_motor.reset_position()
    right_motor.reset_position()
    pid = PID(6.0, 0.0, 0.4)
    while True:
        traveled = (left_motor.position(TURNS) + right_motor.position(TURNS)) / 2 * WHEEL_CIRCUMFERENCE
        error = distance - traveled
        if abs(error) < 0.25:
            break
        power = max(-80, min(80, pid.step(error, 0.02)))
        left_motor.spin(FORWARD, power, PERCENT)
        right_motor.spin(FORWARD, power, PERCENT)
        wait(20, MSEC)
    left_motor.stop(BRAKE)
    right_motor.stop(BRAKE)
    print(""Stopped"", round(error, 2), ""inches from the target"")


drive_inches(36)
";
    }
}
