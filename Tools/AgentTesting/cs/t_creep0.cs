// Sandbox with no program: start pose to "t_proj_start" (t_pose.cs shows drift).
Protobot.RobotCode.RobotProgram.Stop("test");
Protobot.Motion.Sandbox.Stop(); Protobot.Motion.Sandbox.Begin();
var rb = Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb;
UnityEngine.PlayerPrefs.SetString("t_proj_start", rb.position.x + "," + rb.position.z + "," + rb.rotation.eulerAngles.y);
return "ok " + Protobot.Motion.Sandbox.Instance.Bodies().Count() + " bodies, gravity " + UnityEngine.Physics.gravity;
