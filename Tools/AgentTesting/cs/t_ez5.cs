// Driver control: the EZ-Template example's opcontrol_tank() on test sticks (full forward on both for 1.5 s).
Protobot.Motion.Sandbox.TestSticks = () => (new UnityEngine.Vector2(0f, 1f), new UnityEngine.Vector2(0f, 1f));
var code = Protobot.RobotCode.CodeStore.Source;
Protobot.Motion.Sandbox.Stop(); Protobot.Motion.Sandbox.Begin();
var rb = Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb;
UnityEngine.PlayerPrefs.SetString("t_ez_start", rb.position.x + "," + rb.position.z);
var log = new System.Text.StringBuilder();
UnityEngine.PlayerPrefs.SetString("t_code_marks", "");
Protobot.RobotCode.RobotProgram.Printed += s => { log.Append(s); UnityEngine.PlayerPrefs.SetString("t_code_marks", log.ToString()); };
string err; int line;
var ok = Protobot.RobotCode.RobotProgram.Start(code, Protobot.RobotCode.RobotProgram.Period.Driver, 4, out err, out line);
return "started " + ok + " " + err;
