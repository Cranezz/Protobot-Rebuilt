// Temp/look.txt: "fx fy fz pitch yaw dist"
var a = System.IO.File.ReadAllText("Temp/look.txt").Trim().Split(' ');
Protobot.PivotCamera.Main.SetTransform(new Vector3(float.Parse(a[0]), float.Parse(a[1]), float.Parse(a[2])), new Vector3(float.Parse(a[3]), float.Parse(a[4]), 0f), float.Parse(a[5]));
return "ok";
