// Starts the sandbox with a tiny program from PlayerPrefs "t_creep_prog" (Python), start pose to "t_proj_start";
// t_pose.cs then shows whether the robot moves after the program is done.
var code = UnityEngine.PlayerPrefs.GetString("t_creep_prog");
Protobot.Motion.Sandbox.Stop(); Protobot.Motion.Sandbox.Begin();
var rb = Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb;
UnityEngine.PlayerPrefs.SetString("t_proj_start", rb.position.x + "," + rb.position.z + "," + rb.rotation.eulerAngles.y);
string err; int line;
var ok = Protobot.RobotCode.RobotProgram.Start(code, Protobot.RobotCode.RobotProgram.Period.Program, 30, out err, out line);
return "started " + ok + " " + err + " line " + line;
