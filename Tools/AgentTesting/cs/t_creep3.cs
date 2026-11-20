// (Play mode, a build loaded) starts the sandbox with nothing running; start pose and time to "t_creep3"
// (t_creep3r.cs reads the drift per second: a still robot should stay within ~0.01"/s). PlayerPrefs "t_dtacc" =
// "gain,friction" sets the omni hold gain and gearbox friction first (empty: leave them).
var BF = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var set = UnityEngine.PlayerPrefs.GetString("t_dtacc", "");
var gain = typeof(Protobot.Motion.Sandbox).GetField("HoldGain", BF);
var friction = typeof(Protobot.Motion.Sandbox).GetField("GearboxFriction", BF);
if (set != "") { var v = set.Split(','); gain.SetValue(null, float.Parse(v[0])); friction.SetValue(null, float.Parse(v[1])); }
Protobot.RobotCode.RobotProgram.Stop("test");
Protobot.Motion.Sandbox.Stop(); Protobot.Motion.Sandbox.Begin();
var rb = Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb;
UnityEngine.PlayerPrefs.SetString("t_creep3", rb.position.x + "," + rb.position.z + "," + rb.rotation.eulerAngles.y + "," + UnityEngine.Time.time);
return "started, " + Protobot.Motion.Sandbox.Instance.Bodies().Count() + " bodies, gain " + gain.GetValue(null) + " friction " + friction.GetValue(null);
