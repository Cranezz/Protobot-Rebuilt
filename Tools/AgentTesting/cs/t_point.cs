var p = System.IO.File.ReadAllText("Temp/face.txt").Split(',');
var face = new Vector3(float.Parse(p[0]), float.Parse(p[1]), float.Parse(p[2]));
var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
pt.GetMethod("PointAt", new[] { typeof(Vector3) }).Invoke(null, new object[] { face });
pt.GetMethod("Frames").Invoke(null, new object[] { 6 });
return "pointed at " + Camera.main.WorldToScreenPoint(face).ToString("F0");
