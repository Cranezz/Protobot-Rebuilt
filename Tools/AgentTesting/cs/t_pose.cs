var rb = Protobot.Motion.Sandbox.Instance == null ? null : Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb;
var s = UnityEngine.PlayerPrefs.GetString("t_proj_start").Split(',').Select(float.Parse).ToArray();
var p = Protobot.RobotCode.RobotProgram.Current;
return (p == null ? "-" : p.Time.ToString("0.0")) + " x=" + (rb.position.x - s[0]).ToString("0.0") + " y=" + (rb.position.z - s[1]).ToString("0.0") + " turn=" + UnityEngine.Mathf.DeltaAngle(s[2], rb.rotation.eulerAngles.y).ToString("0");
