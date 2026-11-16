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
            ("PID tuning (graph)", "Your own PID loop: open the Graph tab and tune kP, kI, kD", Pid),
            ("Odometry and motions (LemLib style)", "move_to_point, turn_to_heading, move_to_pose and pure pursuit, with odometry", Odometry),
            ("Autonomous selector (touch screen)", "Tap a button on the Brain's screen to pick the autonomous", Selector),
            ("Controller screen and rumble", "Prints on the controller and rumbles when A is pressed", ControllerScreen),
        };

        // V2: an example's "# {devices}" ... "# {/devices}" lines are swapped for the robot's own configuration (Insert
        // devices) when the build has a drivetrain, and {track} {wheel} {wheel_rpm} {deg_per_inch} for its numbers.
        public static string Fill(string code) {
            var start = code.IndexOf("# {devices}", System.StringComparison.Ordinal);
            var end = code.IndexOf("# {/devices}", System.StringComparison.Ordinal);
            string config = null;
            try { config = DeviceConfig.Generate(); }
            catch (System.Exception e) { UnityEngine.Debug.LogWarning("V2: example devices: " + e.Message); }
            var drive = DeviceConfig.LastDrive;
            if (start >= 0 && end > start) {
                var lineEnd = code.IndexOf('\n', end);
                if (lineEnd < 0) lineEnd = code.Length;
                if (drive != null && config != null) {
                    var mine = config.TrimEnd('\n', '\r');
                    if (!drive.Value.imu) mine += "\nimu = None   # (no Inertial sensor on the robot: the heading comes from the drive wheels)";
                    code = code.Substring(0, start) + mine + code.Substring(lineEnd);
                }
                else {
                    var startEnd = code.IndexOf('\n', start);
                    code = code.Substring(0, start) + code.Substring(startEnd + 1, end - startEnd - 1) + code.Substring(System.Math.Min(code.Length, lineEnd + 1));
                }
            }
            string F(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var wheel = drive?.diameter ?? 4f;
            var ratio = drive?.ratio ?? 1f;
            var motorRpm = drive?.motorRpm ?? 200f;
            return code.Replace("{track}", F(drive?.track ?? 12f)).Replace("{wheel}", F(wheel)).Replace("{wheel_rpm}", F(motorRpm / ratio))
                       .Replace("{deg_per_inch}", F(360.0 * ratio / (System.Math.PI * wheel)));
        }

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

        private const string Pid = @"# PID tuning with the Graph tab: it draws every ""name: number"" your program prints.
# Run it, open Graph (under the code), then change kP, kI, kD and run again:
#   kP first (the robot gets there, then rocks), then kD to calm it, kI last for the final bit.
# The same code works on the real robot (the prints go to the terminal).
from vex import *

# {devices}
left_drive = MotorGroup(Motor(Ports.PORT1, GearSetting.RATIO_18_1, False))
right_drive = MotorGroup(Motor(Ports.PORT10, GearSetting.RATIO_18_1, True))
# {/devices}

kP = 0.3
kI = 0.0
kD = 1.5

DEG_PER_INCH = {deg_per_inch}   # motor degrees for one inch of travel (your wheels and gearing)
target = 24 * DEG_PER_INCH         # 24 inches

left_drive.set_position(0, DEGREES)
right_drive.set_position(0, DEGREES)
integral = 0
last_error = target
for step in range(150):            # 3 seconds, every 20 ms
    error = target - (left_drive.position(DEGREES) + right_drive.position(DEGREES)) / 2
    integral += error
    derivative = error - last_error
    last_error = error
    power = kP * error + kI * integral + kD * derivative
    power = max(-100, min(100, power))
    left_drive.spin(FORWARD, power, PERCENT)
    right_drive.spin(FORWARD, power, PERCENT)
    print(""error:"", round(error / DEG_PER_INCH, 2), ""power:"", round(power, 1))
    wait(20, MSEC)
left_drive.stop(BRAKE)
right_drive.stop(BRAKE)
";

        private const string Odometry = @"# Odometry and autonomous motions, the way LemLib / EZ-Template teams write them
# (same names as LemLib). x, y in inches; heading in degrees, clockwise from forward.
from vex import *
from motion import *

# {devices}
left_drive = MotorGroup(Motor(Ports.PORT1, GearSetting.RATIO_6_1, True), Motor(Ports.PORT2, GearSetting.RATIO_6_1, True))
right_drive = MotorGroup(Motor(Ports.PORT3, GearSetting.RATIO_6_1, False), Motor(Ports.PORT4, GearSetting.RATIO_6_1, False))
imu = Inertial(Ports.PORT10)
# {/devices}

# Track width (in), wheel diameter (in), wheel rpm, horizontal drift (2 with omnis all round, 8 with traction wheels)
odom_drive = Drivetrain(left_drive, right_drive, {track}, {wheel}, {wheel_rpm}, 2)
# kP, kI, kD, anti windup, small error, small error timeout (ms), large error, large error timeout (ms), slew
lateral = ControllerSettings(10, 0, 3, 3, 1, 100, 3, 500, 20)
angular = ControllerSettings(2, 0, 10, 3, 1, 100, 3, 500, 0)
# Tracking wheels go here: TrackingWheel(Rotation(Ports.PORT5), Omniwheel.NEW_2, -2.5). None: the drive motors are used.
sensors = OdomSensors(None, None, None, None, imu)
chassis = Chassis(odom_drive, lateral, angular, sensors)

chassis.calibrate()           # the Inertial sensor calibrates (about 2 s)
chassis.set_pose(0, 0, 0)

# Motions run in the background: each waits for the one before. wait=True waits for it to finish.
chassis.move_to_point(0, 24, 2000)
chassis.turn_to_heading(90, 1000)
chassis.move_to_pose(24, 36, 0, 3000)       # boomerang: arrives at (24, 36) facing forward
chassis.wait_until_done()
print(""pose:"", chassis.get_pose())

# Pure pursuit along a path of points, then back to the start backwards
chassis.follow([(24, 36), (12, 48), (0, 48)], 10, 4000)
chassis.move_to_point(0, 0, 4000, forwards=False, wait=True)
print(""back at"", chassis.get_pose())
";

        private const string Selector = @"# Autonomous selector: tap a button on the Brain's screen before the match
# (here: the Brain screen tab under the code, click a button). The competition
# template then runs the one picked.
from vex import *

brain = Brain()
controller = Controller()

autons = [""Left side"", ""Right side"", ""Skills""]
selected = 0


def draw_buttons():
    brain.screen.clear_screen()
    brain.screen.set_font(FontType.MONO20)
    for i in range(len(autons)):
        x = 20 + i * 150
        brain.screen.set_pen_color(Color.WHITE)
        brain.screen.set_fill_color(Color.GREEN if i == selected else Color(60, 60, 60))
        brain.screen.draw_rectangle(x, 60, 140, 80)
        brain.screen.print_at(autons[i], x=x + 12, y=106)
    brain.screen.set_fill_color(Color.BLACK)
    brain.screen.print_at(""Tap an autonomous"", x=20, y=200)


def screen_pressed():
    global selected
    x = brain.screen.x_position()
    y = brain.screen.y_position()
    if 60 <= y <= 140:
        for i in range(len(autons)):
            if 20 + i * 150 <= x <= 160 + i * 150:
                selected = i
                controller.screen.clear_screen()
                controller.screen.print(""Auton: "" + autons[i])
                controller.rumble(""."")
    draw_buttons()


def autonomous():
    brain.screen.clear_screen()
    brain.screen.print(""Running: "" + autons[selected])
    if selected == 0:
        pass   # your left-side routine
    elif selected == 1:
        pass   # your right-side routine
    else:
        pass   # your skills routine


def user_control():
    while True:
        wait(20, MSEC)


brain.screen.pressed(screen_pressed)
draw_buttons()
comp = Competition(user_control, autonomous)
";

        private const string ControllerScreen = @"# The controller's screen (3 rows of 19) and rumble.
from vex import *

brain = Brain()
controller = Controller()
presses = 0


def on_a():
    global presses
    presses += 1
    controller.screen.set_cursor(2, 1)
    controller.screen.print(""A pressed:"", presses, ""   "")
    controller.rumble(""."")   # . short, - long, space a pause


controller.buttonA.pressed(on_a)
controller.screen.clear_screen()
controller.screen.set_cursor(1, 1)
controller.screen.print(""Protobot robot"")
while True:
    controller.screen.set_cursor(3, 1)
    controller.screen.print(""Battery:"", brain.battery.capacity(), ""%  "")
    wait(500, MSEC)
";
    }
}
