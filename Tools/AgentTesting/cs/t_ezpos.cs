var rb = Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb;
var st = UnityEngine.PlayerPrefs.GetString("t_ez_start").Split(',');
var p = Protobot.RobotCode.RobotProgram.Current;
return "moved x=" + (rb.position.x - float.Parse(st[0])).ToString("0.0") + " y=" + (rb.position.z - float.Parse(st[1])).ToString("0.0") + " yaw=" + rb.rotation.eulerAngles.y.ToString("0.0") + " speed=" + rb.velocity.magnitude.ToString("0.0") + " fibers running=" + (p != null && p.vm.Running) + " t=" + (p != null ? p.Time.ToString("0.0") : "-");
