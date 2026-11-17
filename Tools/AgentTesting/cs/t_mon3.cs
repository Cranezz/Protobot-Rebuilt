// Monitor with a PROS C++ program: a Task, globals, a struct, printf colors; opens the Monitor tab.
var code = @"#include ""main.h""
pros::MotorGroup left_mg({4, 5, 6});
pros::MotorGroup right_mg({-1, -2, -3});
int ticks = 0;
double kP = 0.6;
struct Pid { double kp; double ki; double last; };
Pid drivePid = {0.6, 0.01, 0};

void count_task() {
  while (1) {
    ticks++;
    pros::delay(50);
  }
}

double step(double target, double now) {
  double error = target - now;
  drivePid.last = error;
  return error * drivePid.kp;
}

void opcontrol() {
  pros::Task counter(count_task);
  printf(""\033[31mred from C++\033[0m\n"");
  while (1) {
    double out = step(500, left_mg.get_position());
    left_mg.move_velocity(out);
    right_mg.move_velocity(out);
    pros::delay(20);
  }
}
";
Protobot.RobotCode.CodeStore.Set(code);
Protobot.RobotCode.CodePanel.Open();
var panel = UnityEngine.Object.FindAnyObjectByType<Protobot.RobotCode.CodePanel>();
panel.GetType().GetMethod("SetTab", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(int) }, null).Invoke(panel, new object[] { 3 });
panel.GetType().GetMethod("Run", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(panel, null);
return "running " + Protobot.RobotCode.RobotProgram.Active + " " + Protobot.RobotCode.RobotProgram.Current?.Status;
