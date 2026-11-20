// Runs the C++ file in PlayerPrefs "t_hdr_prog" on the loaded build (period "t_hdr_period": a/d), output to
// "t_code_marks", the robot's start pose to "t_proj_start" (t_proj3.cs reads both).
var code = System.IO.File.ReadAllText(UnityEngine.PlayerPrefs.GetString("t_hdr_prog"));
Protobot.RobotCode.CodeStore.Set(code);
Protobot.Motion.Sandbox.Stop(); Protobot.Motion.Sandbox.Begin();
var rb = Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb;
UnityEngine.PlayerPrefs.SetString("t_proj_start", rb.position.x + "," + rb.position.z + "," + rb.rotation.eulerAngles.y);
var log = new System.Text.StringBuilder();
UnityEngine.PlayerPrefs.SetString("t_code_marks", "");
Protobot.RobotCode.RobotProgram.Printed += s => { if (log.Length < 6000) log.Append(s); UnityEngine.PlayerPrefs.SetString("t_code_marks", log.ToString()); };
string err; int line;
var period = UnityEngine.PlayerPrefs.GetString("t_hdr_period") == "d" ? Protobot.RobotCode.RobotProgram.Period.Driver : Protobot.RobotCode.RobotProgram.Period.Autonomous;
var ok = Protobot.RobotCode.RobotProgram.Start(code, period, UnityEngine.PlayerPrefs.GetFloat("t_run_limit", 15), out err, out line);
return "started " + ok + " " + err + " line " + line;
