var bf = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var a = System.IO.File.ReadAllText("Temp/brake.txt").Trim().Split(',');
typeof(Protobot.Motion.Sandbox).GetField("BrakeBand", bf).SetValue(null, float.Parse(a[0]));
if (a.Length > 1) typeof(Protobot.Motion.Sandbox).GetField("HoldGain", bf).SetValue(null, float.Parse(a[1]));
return "BrakeBand " + a[0] + (a.Length > 1 ? " HoldGain " + a[1] : "");
