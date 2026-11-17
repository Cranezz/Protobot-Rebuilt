// Monitor live test: a program with a thread, monitor_variable/monitor_sensor, console colors; opens the Monitor tab.
var code = @"from vex import *
brain = Brain()
left = MotorGroup(Motor(Ports.PORT4, GearSetting.RATIO_6_1), Motor(Ports.PORT5, GearSetting.RATIO_6_1), Motor(Ports.PORT6, GearSetting.RATIO_6_1))
right = MotorGroup(Motor(Ports.PORT1, GearSetting.RATIO_6_1, True), Motor(Ports.PORT2, GearSetting.RATIO_6_1, True), Motor(Ports.PORT3, GearSetting.RATIO_6_1, True))
left_motor = Motor(Ports.PORT4, GearSetting.RATIO_6_1)
kP = 0.8
target = 360
count = 0
history = []

monitor_variable('count', 'target')
monitor_sensor('left_motor.position', 'brain.timer.time')

def counter():
    global count
    while True:
        count += 1
        wait(100, MSEC)

def move_to(goal):
    error = goal - left_motor.position(DEGREES)
    while abs(error) > 5:
        power = kP * error
        left.spin(FORWARD, power, RPM)
        right.spin(FORWARD, power, RPM)
        error = goal - left_motor.position(DEGREES)
        history.append(round(error))
        if len(history) > 6:
            history.pop(0)
        wait(20, MSEC)
    left.stop(BRAKE)
    right.stop(BRAKE)

Thread(counter)
set_console_text_color(Color.RED)
print('red text')
set_console_text_color(Color.BLUE)
print('blue text')
print('\033[32mgreen\033[0m and normal')
move_to(target)
print('done at', round(left_motor.position(DEGREES)))
while True:
    wait(50, MSEC)
";
Protobot.RobotCode.CodeStore.Set(code);
Protobot.RobotCode.CodePanel.Open();
var panel = UnityEngine.Object.FindAnyObjectByType<Protobot.RobotCode.CodePanel>();
panel.GetType().GetMethod("SetTab", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(int) }, null).Invoke(panel, new object[] { 3 });
panel.GetType().GetMethod("Run", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(panel, null);
return "running " + Protobot.RobotCode.RobotProgram.Active + " " + Protobot.RobotCode.RobotProgram.Current?.Status;
