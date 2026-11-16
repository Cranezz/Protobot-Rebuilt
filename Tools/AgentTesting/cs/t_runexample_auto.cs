// Runs example N (PlayerPrefs t_example) on the loaded robot; output goes to PlayerPrefs t_code_marks.
var i = UnityEngine.PlayerPrefs.GetInt("t_example", 0);
var code = Protobot.RobotCode.Examples.Fill(Protobot.RobotCode.Examples.All[i].code);
Protobot.RobotCode.CodeStore.Set(code);
Protobot.Motion.Sandbox.Stop(); Protobot.Motion.Sandbox.Begin();
var log = new System.Text.StringBuilder();
UnityEngine.PlayerPrefs.SetString("t_code_marks", "");
Protobot.RobotCode.RobotProgram.Printed += s => { log.Append(s); if (log.Length < 3000) UnityEngine.PlayerPrefs.SetString("t_code_marks", log.ToString()); };
string err; int line;
var ok = Protobot.RobotCode.RobotProgram.Start(code, Protobot.RobotCode.RobotProgram.Period.Autonomous, 15, out err, out line);
return Protobot.RobotCode.Examples.All[i].name + " started " + ok + " " + err;
