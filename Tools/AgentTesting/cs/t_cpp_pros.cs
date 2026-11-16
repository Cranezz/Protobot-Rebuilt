// A PROS + LemLib program as teams write it (after LemLib's example main.cpp), on the 6-motor drive.
var code = @"#include ""main.h""
#include ""lemlib/api.hpp"" // IWYU pragma: keep

pros::Controller controller(pros::E_CONTROLLER_MASTER);

// motor groups
pros::MotorGroup leftMotors({4, 5, 6}, pros::MotorGearset::blue);
pros::MotorGroup rightMotors({-1, -2, -3}, pros::MotorGearset::blue);

// drivetrain settings
lemlib::Drivetrain drivetrain(&leftMotors, // left motor group
                              &rightMotors, // right motor group
                              14.125, // track width
                              lemlib::Omniwheel::NEW_325, // using new 3.25"" omnis
                              360, // drivetrain rpm
                              2 // horizontal drift
);

lemlib::ControllerSettings linearController(10, 0, 3, 3, 1, 100, 3, 500, 20);
lemlib::ControllerSettings angularController(2, 0, 10, 3, 1, 100, 3, 500, 0);
lemlib::OdomSensors sensors(nullptr, nullptr, nullptr, nullptr, nullptr);
lemlib::ExpoDriveCurve throttleCurve(3, 10, 1.019);
lemlib::ExpoDriveCurve steerCurve(3, 10, 1.019);
lemlib::Chassis chassis(drivetrain, linearController, angularController, sensors, &throttleCurve, &steerCurve);

void initialize() {
    pros::lcd::initialize();
    chassis.calibrate();
    pros::Task screenTask([&]() {
        while (1) {
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
    chassis.waitUntilDone();
    printf(""mark A %.2f %.2f %.2f\n"", chassis.getPose().x, chassis.getPose().y, chassis.getPose().theta);
    chassis.turnToHeading(90, 2000, {.direction = lemlib::AngularDirection::CW_CLOCKWISE, .maxSpeed = 100});
    chassis.waitUntilDone();
    printf(""mark B %.2f %.2f %.2f\n"", chassis.getPose().x, chassis.getPose().y, chassis.getPose().theta);
    chassis.moveToPose(24, 30, 90, 4000, {.forwards = true, .lead = 0.3}, false);
    printf(""mark C %.2f %.2f %.2f\n"", chassis.getPose().x, chassis.getPose().y, chassis.getPose().theta);
}

void opcontrol() {
    while (1) {
        int leftY = controller.get_analog(pros::E_CONTROLLER_ANALOG_LEFT_Y);
        int rightX = controller.get_analog(pros::E_CONTROLLER_ANALOG_RIGHT_X);
        chassis.arcade(leftY, rightX);
        pros::delay(25);
    }
}
";
Protobot.RobotCode.CodeStore.Set(code);
Protobot.Motion.Sandbox.Stop(); Protobot.Motion.Sandbox.Begin();
var rb = Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb;
var start = rb.position;
var log = new System.Text.StringBuilder();
UnityEngine.PlayerPrefs.SetString("t_code_marks", "");
Protobot.RobotCode.RobotProgram.Printed += s => { log.Append(s.StartsWith("mark") && rb != null ? s.TrimEnd() + "  TRUE x=" + (rb.position - start).x.ToString("0.0") + " y=" + (rb.position - start).z.ToString("0.0") + " yaw=" + rb.rotation.eulerAngles.y.ToString("0.0") + "\n" : s); UnityEngine.PlayerPrefs.SetString("t_code_marks", log.ToString()); };
string err; int line;
var ok = Protobot.RobotCode.RobotProgram.Start(code, Protobot.RobotCode.RobotProgram.Period.Autonomous, 15, out err, out line);
return "started " + ok + " " + err;
