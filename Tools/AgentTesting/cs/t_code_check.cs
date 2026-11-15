var p = Protobot.RobotCode.RobotProgram.Current;
var rb = Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb;
var s = UnityEngine.PlayerPrefs.GetString("t_code_start").Split(',');
var dx = rb.position.x - float.Parse(s[0]); var dz = rb.position.z - float.Parse(s[1]);
var txt = (UnityEngine.Object)UnityEngine.GameObject.Find("Code Panel");
var outText = UnityEngine.GameObject.Find("Code Panel").GetComponentsInChildren<TMPro.TextMeshProUGUI>(true).First(t => t.name == "Text" && t.transform.parent.name == "Content").text;
return "status " + (p == null ? "none" : p.Status + " t=" + p.Time.ToString("0.00")) + " moved " + System.Math.Sqrt(dx*dx+dz*dz).ToString("0.0") + "in (dx " + dx.ToString("0.0") + " dz " + dz.ToString("0.0") + ")\nOUTPUT:\n" + outText;
