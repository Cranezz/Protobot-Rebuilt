// Test sticks: PlayerPrefs "t_sticks" = "lx,ly,rx,ry" (empty: off).
var s = UnityEngine.PlayerPrefs.GetString("t_sticks");
if (string.IsNullOrEmpty(s)) { Protobot.Motion.Sandbox.TestSticks = null; return "off"; }
var v = s.Split(',').Select(float.Parse).ToArray();
Protobot.Motion.Sandbox.TestSticks = () => (new UnityEngine.Vector2(v[0], v[1]), new UnityEngine.Vector2(v[2], v[3]));
return "sticks " + s;
