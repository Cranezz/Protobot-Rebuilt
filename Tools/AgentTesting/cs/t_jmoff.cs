// (Play) Sandbox.JointMotorsOff (and RotorInertia 0) from Temp/jmoff.txt: "1" = the old motor model, "0" = the new one. Applies at the next Simulate.
var off = System.IO.File.ReadAllText("Temp/jmoff.txt").Trim() == "1";
var t = typeof(Protobot.Motion.Sandbox);
var bf = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
t.GetField("JointMotorsOff", bf).SetValue(null, off);
t.GetField("RotorInertia", bf).SetValue(null, off ? 0f : 1e-6f);
return "JointMotorsOff " + off;
