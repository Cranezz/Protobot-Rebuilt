// (Play) Sandbox.JointDriveSign from Temp/jsign.txt (+1 / -1), for checking the hinge drives' direction.
var f = typeof(Protobot.Motion.Sandbox).GetField("JointDriveSign", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
f.SetValue(null, float.Parse(System.IO.File.ReadAllText("Temp/jsign.txt").Trim(), System.Globalization.CultureInfo.InvariantCulture));
return "JointDriveSign " + f.GetValue(null);
