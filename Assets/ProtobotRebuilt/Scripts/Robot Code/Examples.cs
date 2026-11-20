using System.Linq;

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
            ("Vision: drive to a color", "Turns toward the largest object of a Vision signature and drives up to it", Vision),
            ("GPS: drive to points", "Drives to points on the field by the GPS Sensor (in a game: File > Play game)", Gps),
            ("PID tuning (graph)", "Your own PID loop: open the Graph tab and tune kP, kI, kD", Pid),
            ("Odometry and motions (LemLib style)", "move_to_point, turn_to_heading, move_to_pose and pure pursuit, with odometry", Odometry),
            ("Autonomous selector (touch screen)", "Tap a button on the Brain's screen to pick the autonomous", Selector),
            ("Controller screen and rumble", "Prints on the controller and rumbles when A is pressed", ControllerScreen),
            ("C++: PROS + LemLib", "A PROS project's main.cpp with LemLib odometry and motions (as LemLib's example)", ProsLemLib),
            ("C++: VEXcode competition template", "VEXcode C++'s competition template with a drivetrain", VexcodeCpp),
            ("C++: PROS + EZ-Template", "EZ-Template's example project: PID drive, turns, swings, motion chaining, odometry and the auton selector", EzTemplate),
        };

        // V2: an example's "# {devices}" ... "# {/devices}" lines are swapped for the robot's own configuration (Insert
        // devices) when the build has a drivetrain, and {track} {wheel} {wheel_rpm} {deg_per_inch} for its numbers.
        public static string Fill(string code) {
            var cpp = Scripting.CppTranslator.LooksLikeCpp(code);
            var start = code.IndexOf(cpp ? "// {devices}" : "# {devices}", System.StringComparison.Ordinal);
            var end = code.IndexOf(cpp ? "// {/devices}" : "# {/devices}", System.StringComparison.Ordinal);
            string config = null;
            string visionName = "vision_5", sigName = "vision_5__SIG_1", gpsName = "gps_8";
            try { config = cpp ? DeviceConfig.GenerateCpp(!DeviceConfig.UsesVexcode(code), DeviceConfig.UsesEz(code)) : DeviceConfig.Generate(); }
            catch (System.Exception e) { UnityEngine.Debug.LogWarning("V2: example devices: " + e.Message); }
            var drive = DeviceConfig.LastDrive;
            if (start >= 0 && end > start) {
                var lineEnd = code.IndexOf('\n', end);
                if (lineEnd < 0) lineEnd = code.Length;
                if (drive != null && config != null) {
                    var mine = config.TrimEnd('\n', '\r');
                    // V2: the Vision example names the robot's own sensor and its first signature (none: the example's).
                    if (code.Contains("{vision}")) {
                        var vis = Devices.V5Brain.Devices.OfType<Devices.VisionSensor>().FirstOrDefault(v => v.OnBrain);
                        if (vis != null) {
                            visionName = DeviceConfig.Name(Motion.Controls.GivenName(vis.part) ?? "vision_" + vis.port, new System.Collections.Generic.HashSet<string>());
                            var first = Devices.VisionSensor.Saved(vis.part).FirstOrDefault(s => s.HasValue);
                            sigName = first.HasValue ? visionName + "__" + Devices.VisionSignature.Clean(first.Value.name) : "Signature(1, 8000, 11000, 9500, -1500, -500, -1000, 2.5, 0)";
                        }
                        else mine += "\n" + DefaultVision + "   # (no Vision Sensor on the robot yet: add one from Electronics)";
                    }
                    // V2: the GPS example names the robot's own GPS Sensor (none: the example's).
                    if (code.Contains("{gps}")) {
                        var gps = Devices.V5Brain.Devices.OfType<Devices.GpsSensor>().FirstOrDefault(v => v.OnBrain);
                        if (gps != null) gpsName = DeviceConfig.Name(Motion.Controls.GivenName(gps.part) ?? "gps_" + gps.port, new System.Collections.Generic.HashSet<string>());
                        else mine += "\ngps_8 = Gps(Ports.PORT8, 0.00, 0.00, MM, 180, RIGHT)   # (no GPS Sensor on the robot yet: add one from Electronics)";
                    }
                    if (!drive.Value.imu) mine += cpp ? (DeviceConfig.UsesVexcode(code) || DeviceConfig.UsesEz(code) ? "" : "\n// (no Inertial sensor on the robot: LemLib takes the heading from the drive wheels)")
                                                      : "\nimu = None   # (no Inertial sensor on the robot: the heading comes from the drive wheels)";
                    code = code.Substring(0, start) + mine + code.Substring(lineEnd);
                }
                else {
                    var startEnd = code.IndexOf('\n', start);
                    code = code.Substring(0, start) + code.Substring(startEnd + 1, end - startEnd - 1) + code.Substring(System.Math.Min(code.Length, lineEnd + 1));
                }
            }
            code = code.Replace("{vision}", visionName).Replace("{sig}", sigName).Replace("{gps}", gpsName);
            string F(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var wheel = drive?.diameter ?? 4f;
            var ratio = drive?.ratio ?? 1f;
            var motorRpm = drive?.motorRpm ?? 200f;
            var imuArg = drive != null && !drive.Value.imu ? "nullptr" : "&imu";
            var wheelRpm = motorRpm / ratio;
            if (System.Math.Abs(wheelRpm - System.Math.Round(wheelRpm)) < 0.5) wheelRpm = (float)System.Math.Round(wheelRpm);
            return code.Replace("{imu}", imuArg).Replace("{track}", F(drive?.track ?? 12f)).Replace("{wheel}", F(wheel)).Replace("{wheel_rpm}", F(wheelRpm))
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

        private const string DefaultVision = "vision_5__SIG_1 = Signature(1, 8000, 11000, 9500, -1500, -500, -1000, 2.5, 0)\nvision_5 = Vision(Ports.PORT5, 50, vision_5__SIG_1)";

        // V2: the Vision Sensor, as VEXcode's vision tutorials use it: snapshot a signature, steer by the largest
        // object's centerX, stop when it's big (close).
        private const string Vision = @"# Vision Sensor: turns toward the largest object of a color and drives up to it.
# First make the color's signature: Edit > Devices..., open the Vision Sensor's card > Vision Utility,
# drag a box over the object and Set signature 1. Then run this (Insert devices puts your robot's in).
from vex import *

brain = Brain()
# {devices}
left_drive = MotorGroup(Motor(Ports.PORT1, GearSetting.RATIO_18_1, False))
right_drive = MotorGroup(Motor(Ports.PORT10, GearSetting.RATIO_18_1, True))
vision_5__SIG_1 = Signature(1, 8000, 11000, 9500, -1500, -500, -1000, 2.5, 0)
vision_5 = Vision(Ports.PORT5, 50, vision_5__SIG_1)
# {/devices}

CENTER = 158     # the middle of the picture (it's 316 wide, 212 high)
CLOSE = 140      # wide enough on the picture: we're there

while True:
    objects = {vision}.take_snapshot({sig})
    brain.screen.clear_screen()
    brain.screen.set_cursor(1, 1)
    if objects:
        target = objects[0]          # the largest
        turn = (target.centerX - CENTER) * 0.25   # to the right of the middle: turn right
        forward = 0 if target.width > CLOSE else 30
        left_drive.spin(FORWARD, forward + turn, PERCENT)
        right_drive.spin(FORWARD, forward - turn, PERCENT)
        brain.screen.print(""x"", target.centerX, ""width"", target.width)
    else:
        left_drive.spin(FORWARD, 12, PERCENT)    # look round for it
        right_drive.spin(REVERSE, 12, PERCENT)
        brain.screen.print(""looking..."")
    wait(20, MSEC)
";

        // V2: the GPS Sensor, as VEX's GPS tutorials use it: turn toward a point, drive while it's far, check quality.
        private const string Gps = @"# GPS Sensor: drives to points on the field.
# The GPS reads the black-and-white code strip on the field's walls, so try it in a game (File > Play game...).
# (0, 0) is the field's middle; +X toward the blue drivers, +Y away from the audience; heading 0 = the far wall,
# clockwise. Mount it level, high enough to see over game pieces, facing a wall (VEX: on the back, facing back).
from vex import *
import math

brain = Brain()
# {devices}
left_drive = MotorGroup(Motor(Ports.PORT1, GearSetting.RATIO_18_1, False))
right_drive = MotorGroup(Motor(Ports.PORT10, GearSetting.RATIO_18_1, True))
gps_8 = Gps(Ports.PORT8, 0.00, 0.00, MM, 180, RIGHT)
# {/devices}

POINTS = [(-24, -24), (24, -24), (0, 24)]   # inches from the field's middle

def drive_to(x, y):
    while True:
        if {gps}.quality() < 90:          # can't see the strip: stop rather than guess
            left_drive.stop()
            right_drive.stop()
            wait(20, MSEC)
            continue
        dx = x - {gps}.x_position(INCHES)
        dy = y - {gps}.y_position(INCHES)
        distance = math.sqrt(dx * dx + dy * dy)
        if distance < 2:
            break
        target = math.degrees(math.atan2(dx, dy))                 # clockwise from +Y, like the GPS
        error = (target - {gps}.heading() + 540) % 360 - 180      # -180 to 180
        turn = error * 0.6
        forward = min(50, distance * 3) if abs(error) < 45 else 0
        left_drive.spin(FORWARD, forward + turn, PERCENT)
        right_drive.spin(FORWARD, forward - turn, PERCENT)
        brain.screen.clear_screen()
        brain.screen.set_cursor(1, 1)
        brain.screen.print(""x"", round({gps}.x_position(INCHES), 1), ""y"", round({gps}.y_position(INCHES), 1))
        brain.screen.next_row()
        brain.screen.print(""heading"", round({gps}.heading()), ""quality"", {gps}.quality())
        wait(20, MSEC)
    left_drive.stop()
    right_drive.stop()

for (x, y) in POINTS:
    drive_to(x, y)
    print(""at"", x, y)
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

        private const string ProsLemLib = @"// A PROS project's main.cpp with LemLib (as LemLib's example project). Units: inches and degrees,
// heading clockwise from forward. In the simulation the competition runs autonomous() or opcontrol()
// after initialize(), by the period picked beside Run.
#include ""main.h""
#include ""lemlib/api.hpp""

// {devices}
pros::Controller controller(pros::E_CONTROLLER_MASTER);
pros::MotorGroup left_drive({-1, -2, -3}, pros::MotorGearset::blue);
pros::MotorGroup right_drive({4, 5, 6}, pros::MotorGearset::blue);
pros::Imu imu(10);
// {/devices}

// track width, wheel diameter, rpm at the wheels, horizontal drift (2 with omnis all round, 8 with traction wheels)
lemlib::Drivetrain drivetrain(&left_drive, &right_drive, {track}, {wheel}, {wheel_rpm}, 2);
// kP, kI, kD, anti windup, small error, small error timeout, large error, large error timeout, slew
lemlib::ControllerSettings linearController(10, 0, 3, 3, 1, 100, 3, 500, 20);
lemlib::ControllerSettings angularController(2, 0, 10, 3, 1, 100, 3, 500, 0);
// tracking wheels (none here: the drive motors are used) and the IMU
lemlib::OdomSensors sensors(nullptr, nullptr, nullptr, nullptr, {imu});
lemlib::ExpoDriveCurve throttleCurve(3, 10, 1.019);
lemlib::ExpoDriveCurve steerCurve(3, 10, 1.019);
lemlib::Chassis chassis(drivetrain, linearController, angularController, sensors, &throttleCurve, &steerCurve);

void initialize() {
    pros::lcd::initialize();
    chassis.calibrate();
    // The pose on the Brain's screen
    pros::Task screenTask([&]() {
        while (true) {
            pros::lcd::print(0, ""X: %f"", chassis.getPose().x);
            pros::lcd::print(1, ""Y: %f"", chassis.getPose().y);
            pros::lcd::print(2, ""Theta: %f"", chassis.getPose().theta);
            pros::delay(50);
        }
    });
}

void disabled() {}
void competition_initialize() {}

void autonomous() {
    chassis.setPose(0, 0, 0);
    chassis.moveToPoint(0, 24, 3000);
    chassis.turnToHeading(90, 1500);
    chassis.moveToPose(24, 36, 0, 4000, {.lead = 0.4});
    chassis.waitUntilDone();
    chassis.moveToPoint(0, 0, 4000, {.forwards = false}, false);
}

void opcontrol() {
    while (true) {
        int leftY = controller.get_analog(pros::E_CONTROLLER_ANALOG_LEFT_Y);
        int rightX = controller.get_analog(pros::E_CONTROLLER_ANALOG_RIGHT_X);
        chassis.arcade(leftY, rightX);
        pros::delay(25);
    }
}
";

        private const string EzTemplate = @"// EZ-Template's example project (main.cpp and autons.cpp in one file). Speeds are 0 to 127, distances in
// inches (24_in), angles in degrees, clockwise. Pick the auton on the Brain's screen (touch its left or right
// side), then run Autonomous beside Run; Driver runs opcontrol() with tank drive.
#include ""main.h""

// {devices}
ez::Drive chassis({-1, -2, -3}, {4, 5, 6}, 10, {wheel}, {wheel_rpm});
// {/devices}

const int DRIVE_SPEED = 110;
const int TURN_SPEED = 90;
const int SWING_SPEED = 110;

void default_constants() {
  // P, I, D, and start I
  chassis.pid_drive_constants_set(20.0, 0.0, 100.0);
  chassis.pid_heading_constants_set(11.0, 0.0, 20.0);
  chassis.pid_turn_constants_set(3.0, 0.05, 20.0, 15.0);
  chassis.pid_swing_constants_set(6.0, 0.0, 65.0);
  chassis.pid_odom_angular_constants_set(6.5, 0.0, 52.5);
  chassis.pid_odom_boomerang_constants_set(5.8, 0.0, 32.5);

  // Exit conditions: small error time and size, big error time and size, not moving time, current time
  chassis.pid_turn_exit_condition_set(90_ms, 3_deg, 250_ms, 7_deg, 500_ms, 500_ms);
  chassis.pid_swing_exit_condition_set(90_ms, 3_deg, 250_ms, 7_deg, 500_ms, 500_ms);
  chassis.pid_drive_exit_condition_set(90_ms, 1_in, 250_ms, 3_in, 500_ms, 500_ms);
  chassis.pid_odom_turn_exit_condition_set(90_ms, 3_deg, 250_ms, 7_deg, 500_ms, 750_ms);
  chassis.pid_odom_drive_exit_condition_set(90_ms, 1_in, 250_ms, 3_in, 500_ms, 750_ms);
  chassis.pid_turn_chain_constant_set(3_deg);
  chassis.pid_swing_chain_constant_set(5_deg);
  chassis.pid_drive_chain_constant_set(3_in);

  // Slew: start at this speed and reach full speed over this distance
  chassis.slew_turn_constants_set(3_deg, 70);
  chassis.slew_drive_constants_set(3_in, 70);
  chassis.slew_swing_constants_set(3_in, 80);

  chassis.odom_turn_bias_set(0.9);
  chassis.odom_look_ahead_set(7_in);
  chassis.odom_boomerang_distance_set(16_in);
  chassis.odom_boomerang_dlead_set(0.625);
  chassis.pid_angle_behavior_set(ez::shortest);
}

void drive_example() {
  chassis.pid_drive_set(24_in, DRIVE_SPEED, true);
  chassis.pid_wait();
  chassis.pid_drive_set(-12_in, DRIVE_SPEED);
  chassis.pid_wait();
  chassis.pid_drive_set(-12_in, DRIVE_SPEED);
  chassis.pid_wait();
}

void turn_example() {
  chassis.pid_turn_set(90_deg, TURN_SPEED);
  chassis.pid_wait();
  chassis.pid_turn_set(45_deg, TURN_SPEED);
  chassis.pid_wait();
  chassis.pid_turn_set(0_deg, TURN_SPEED);
  chassis.pid_wait();
}

void swing_example() {
  chassis.pid_swing_set(ez::LEFT_SWING, 45_deg, SWING_SPEED, 45);
  chassis.pid_wait();
  chassis.pid_swing_set(ez::RIGHT_SWING, 0_deg, SWING_SPEED, 45);
  chassis.pid_wait();
}

void motion_chaining() {
  // Each wait_quick_chain moves on just before the target, so the robot doesn't stop between motions
  chassis.pid_drive_set(24_in, DRIVE_SPEED, true);
  chassis.pid_wait();
  chassis.pid_turn_set(45_deg, TURN_SPEED);
  chassis.pid_wait_quick_chain();
  chassis.pid_turn_set(-45_deg, TURN_SPEED);
  chassis.pid_wait_quick_chain();
  chassis.pid_turn_set(0_deg, TURN_SPEED);
  chassis.pid_wait();
  chassis.pid_drive_set(-24_in, DRIVE_SPEED, true);
  chassis.pid_wait();
}

void odom_pure_pursuit_example() {
  // Drive through these points, then back to the start
  chassis.pid_odom_set({{{0_in, 24_in}, fwd, DRIVE_SPEED},
                        {{12_in, 24_in}, fwd, DRIVE_SPEED},
                        {{24_in, 24_in}, fwd, DRIVE_SPEED}},
                       true);
  chassis.pid_wait();
  chassis.pid_odom_set({{0_in, 0_in}, rev, DRIVE_SPEED}, true);
  chassis.pid_wait();
}

void odom_boomerang_example() {
  // Arrive at (0, 24) facing 45 degrees, then back
  chassis.pid_odom_set({{0_in, 24_in, 45_deg}, fwd, DRIVE_SPEED}, true);
  chassis.pid_wait();
  chassis.pid_odom_set({{0_in, 0_in, 0_deg}, rev, DRIVE_SPEED}, true);
  chassis.pid_wait();
}

void initialize() {
  ez::ez_template_print();
  pros::delay(500);
  chassis.opcontrol_curve_buttons_toggle(true);
  chassis.opcontrol_drive_activebrake_set(0.0);
  chassis.opcontrol_curve_default_set(0.0, 0.0);
  default_constants();

  ez::as::auton_selector.autons_add({
      {""Drive\n\nDrive forward and come back"", drive_example},
      {""Turn\n\nTurn 3 times"", turn_example},
      {""Swing turn\n\nSwing in an S curve"", swing_example},
      {""Motion chaining\n\nDrive, turns and drive without stopping"", motion_chaining},
      {""Pure pursuit\n\nThrough three points and back"", odom_pure_pursuit_example},
      {""Boomerang\n\nGo to a pose and back"", odom_boomerang_example},
  });
  chassis.initialize();
  ez::as::initialize();
  master.rumble(chassis.drive_imu_calibrated() ? ""."" : ""---"");
}

void disabled() {}
void competition_initialize() {}

void autonomous() {
  chassis.pid_targets_reset();                // Resets PID targets to 0
  chassis.drive_imu_reset();                  // Reset gyro position to 0
  chassis.drive_sensor_reset();               // Reset drive sensors to 0
  chassis.odom_xyt_set(0_in, 0_in, 0_deg);    // Set the current position
  chassis.drive_brake_set(MOTOR_BRAKE_HOLD);  // Hold: helps autonomous consistency
  ez::as::auton_selector.selected_auton_call();
}

void opcontrol() {
  chassis.drive_brake_set(MOTOR_BRAKE_COAST);
  while (true) {
    chassis.opcontrol_tank();  // Tank control
    // chassis.opcontrol_arcade_standard(ez::SPLIT);   // Standard split arcade
    // chassis.opcontrol_arcade_standard(ez::SINGLE);  // Standard single arcade
    pros::delay(ez::util::DELAY_TIME);
  }
}
";

        private const string VexcodeCpp = @"// VEXcode C++ competition template with a drivetrain.
#include ""vex.h""
using namespace vex;

// {devices}
brain Brain;
controller Controller1 = controller(primary);
motor LeftMotor = motor(PORT1, ratio18_1, false);
motor RightMotor = motor(PORT10, ratio18_1, true);
motor_group left_drive = motor_group(LeftMotor);
motor_group right_drive = motor_group(RightMotor);
drivetrain Drivetrain = drivetrain(left_drive, right_drive, 12.57, 12, 12, inches, 1);
// {/devices}

competition Competition;

void pre_auton(void) {
  Brain.Screen.clearScreen();
  Brain.Screen.print(""Ready"");
}

void autonomous(void) {
  Drivetrain.setDriveVelocity(50, percent);
  Drivetrain.driveFor(forward, 24, inches);
  Drivetrain.turnFor(right, 90, degrees);
  Drivetrain.driveFor(reverse, 12, inches);
}

void usercontrol(void) {
  while (true) {
    int forwardPct = Controller1.Axis3.position(percent);
    int turnPct = Controller1.Axis1.position(percent);
    left_drive.spin(forward, forwardPct + turnPct, percent);
    right_drive.spin(forward, forwardPct - turnPct, percent);
    wait(20, msec);
  }
}

int main() {
  Competition.autonomous(autonomous);
  Competition.drivercontrol(usercontrol);
  pre_auton();
  while (true) {
    wait(100, msec);
  }
}
";
    }
}