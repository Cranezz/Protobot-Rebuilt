var rb = Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb;
var p = Protobot.RobotCode.RobotProgram.Current;
var prev = UnityEngine.PlayerPrefs.GetString("t_code_mark", "");
UnityEngine.PlayerPrefs.SetString("t_code_mark", p.Time + "," + rb.position.z);
if (prev == "") return "marked";
var s = prev.Split(',');
var dt = p.Time - double.Parse(s[0]); var dz = rb.position.z - float.Parse(s[1]);
UnityEngine.PlayerPrefs.DeleteKey("t_code_mark");
return "dt " + dt.ToString("0.00") + " dz " + dz.ToString("0.0") + " => " + (dz / dt).ToString("0.0") + " in/s; speed now " + rb.linearVelocity.magnitude.ToString("0.0") + " frame dt " + Time.deltaTime + " fixed " + Time.fixedDeltaTime + " timescale " + Time.timeScale;
