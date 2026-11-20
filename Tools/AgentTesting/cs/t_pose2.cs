var rb = Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb;
var s = UnityEngine.PlayerPrefs.GetString("t_proj_start").Split(',').Select(float.Parse).ToArray();
return "x=" + (rb.position.x - s[0]).ToString("0.00") + " y=" + (rb.position.z - s[1]).ToString("0.00") + " v=" + rb.linearVelocity.ToString("F3") + " w=" + rb.angularVelocity.ToString("F3") + " sleeping " + rb.IsSleeping();
