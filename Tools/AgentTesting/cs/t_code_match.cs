var code = @"from vex import *
left = MotorGroup(Motor(Ports.PORT4), Motor(Ports.PORT5), Motor(Ports.PORT6))
right = MotorGroup(Motor(Ports.PORT1, True), Motor(Ports.PORT2, True), Motor(Ports.PORT3, True))
dt = DriveTrain(left, right, 3.25 * 3.14159, 12.5, 10, INCHES, 60 / 36)

def autonomous():
    print('auton start')
    dt.drive_for(FORWARD, 24, INCHES, 50, PERCENT)
    print('auton done')

def driver():
    print('driver start')

comp = Competition(driver, autonomous)
";
Protobot.RobotCode.CodeStore.Set(code);
var log = new System.Collections.Generic.List<string>();
Protobot.RobotCode.RobotProgram.Printed += s => { log.Add(s.Trim()); UnityEngine.PlayerPrefs.SetString("t_code_marks", string.Join(" | ", log)); };
UnityEditor.EditorApplication.isPaused = false;
var def = Resources.LoadAll<Protobot.Game.GameDefinition>("Games")[0];
Protobot.Game.SkillsGame.Open(def, true, Protobot.Game.SkillsGame.Mode.Match);
var g = Protobot.Game.SkillsGame.Current;
g.SetSkipAutonomous(false);
return "opened, start " + g.x + "," + g.y;
