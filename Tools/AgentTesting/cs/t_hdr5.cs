// Runs the C++ file named in PlayerPrefs "t_hdr_prog" on the loaded build (period from "t_hdr_period": a = autonomous,
// d = driver) and logs everything it prints (warnings included) to "t_code_marks" (read with t_marks.cs).
var path = UnityEngine.PlayerPrefs.GetString("t_hdr_prog");
var code = System.IO.File.ReadAllText(path);
Protobot.RobotCode.CodeStore.Set(code);
Protobot.Motion.Sandbox.Stop(); Protobot.Motion.Sandbox.Begin();
var log = new System.Text.StringBuilder();
UnityEngine.PlayerPrefs.SetString("t_code_marks", "");
Protobot.RobotCode.RobotProgram.Printed += s => { log.Append(s); UnityEngine.PlayerPrefs.SetString("t_code_marks", log.ToString()); };
string err; int line;
var period = UnityEngine.PlayerPrefs.GetString("t_hdr_period") == "d" ? Protobot.RobotCode.RobotProgram.Period.Driver : Protobot.RobotCode.RobotProgram.Period.Autonomous;
var ok = Protobot.RobotCode.RobotProgram.Start(code, period, 15, out err, out line);
return "started " + ok + " " + err + " line " + line;
