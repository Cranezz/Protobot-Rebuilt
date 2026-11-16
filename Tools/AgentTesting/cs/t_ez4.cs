// The EZ-Template example as the app fills it in: page 4 (motion chaining) in autonomous.
string raw = null;
foreach (var e in Protobot.RobotCode.Examples.All) if (e.name.Contains("EZ-Template")) raw = e.code;
var code = Protobot.RobotCode.Examples.Fill(raw);
UnityEngine.PlayerPrefs.SetInt("V2.Code.EzAuton", 3);
Protobot.RobotCode.CodeStore.Set(code);
Protobot.Motion.Sandbox.Stop(); Protobot.Motion.Sandbox.Begin();
var rb0 = Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb;
UnityEngine.PlayerPrefs.SetString("t_ez_start", rb0.position.x + "," + rb0.position.z);
var log = new System.Text.StringBuilder();
UnityEngine.PlayerPrefs.SetString("t_code_marks", "");
Protobot.RobotCode.RobotProgram.Printed += s => { log.Append(s); UnityEngine.PlayerPrefs.SetString("t_code_marks", log.ToString()); };
string err; int line;
var ok = Protobot.RobotCode.RobotProgram.Start(code, Protobot.RobotCode.RobotProgram.Period.Autonomous, 15, out err, out line);
var cfg = code.Substring(code.IndexOf("ez::Drive"), 80);
return "started " + ok + " " + err + "\n" + cfg;
