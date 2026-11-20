var rb = Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb;
var s = UnityEngine.PlayerPrefs.GetString("t_blk_start").Split(',').Select(float.Parse).ToArray();
Protobot.Motion.Sandbox.TestSticks = null;
var p = Protobot.RobotCode.RobotProgram.Current;
var r = "moved x " + (rb.position.x - s[0]).ToString("0.0") + " z " + (rb.position.z - s[1]).ToString("0.0") + " turned " + Mathf.DeltaAngle(s[2], rb.rotation.eulerAngles.y).ToString("0.0") + "; program " + (p == null ? "none" : p.Status);
Protobot.RobotCode.RobotProgram.Stop("test");
return r;
