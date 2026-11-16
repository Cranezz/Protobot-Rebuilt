var code = @"from vex import *
brain = Brain()
controller = Controller()
brain.screen.set_fill_color(Color.BLUE)
brain.screen.set_pen_color(Color.WHITE)
brain.screen.set_pen_width(3)
brain.screen.draw_rectangle(20, 40, 140, 80)
brain.screen.set_fill_color(Color(255, 128, 0))
brain.screen.draw_circle(300, 120, 50)
brain.screen.set_pen_color(Color.GREEN)
brain.screen.draw_line(0, 239, 479, 0)
brain.screen.set_pen_color(Color.WHITE)
brain.screen.set_font(FontType.MONO20)
brain.screen.set_cursor(1, 1)
brain.screen.print('Auton: Left side')
brain.screen.set_font(FontType.PROP30)
brain.screen.set_pen_color(Color.YELLOW)
brain.screen.print_at('Protobot', x=200, y=230, opaque=False)
brain.screen.set_font(FontType.MONO20)
brain.screen.set_pen_color(Color.WHITE)
controller.screen.print('Battery: ', brain.battery.capacity())
controller.screen.next_row()
controller.screen.print('Ready')
controller.rumble('.-.')
def touched():
    print('touched at', brain.screen.x_position(), brain.screen.y_position())
brain.screen.pressed(touched)
i = 0
while True:
    brain.screen.set_cursor(12, 1)
    brain.screen.print('Time:', round(brain.timer.time(SECONDS), 1), '   ')
    i += 1
    wait(100, MSEC)
";
Protobot.RobotCode.CodeStore.Set(code);
Protobot.RobotCode.CodePanel.Close();
Protobot.RobotCode.CodePanel.Open();
var panel = typeof(Protobot.RobotCode.CodePanel).GetField("open", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
typeof(Protobot.RobotCode.CodePanel).GetMethod("SetTab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(panel, new object[] { true });
typeof(Protobot.RobotCode.CodePanel).GetMethod("Run", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(panel, null);
return "running " + (Protobot.RobotCode.RobotProgram.Current != null);
