// Runs the code window's (blocks) program again from the build's pose, start pose to "t_proj_start" (t_pose.cs reads it).
var code = Protobot.RobotCode.CodeStore.Source;
Protobot.Motion.Sandbox.Stop(); Protobot.Motion.Sandbox.Begin();
var rb = Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb;
UnityEngine.PlayerPrefs.SetString("t_proj_start", rb.position.x + "," + rb.position.z + "," + rb.rotation.eulerAngles.y);
string err; int line;
var ok = Protobot.RobotCode.RobotProgram.Start(code, Protobot.RobotCode.RobotProgram.Period.Program, 30, out err, out line);
return "started " + ok + " " + err + " line " + line;
