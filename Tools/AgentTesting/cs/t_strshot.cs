// (Play) A picture of the string rig: free camera at the arm from above and in front. Temp/strshot.txt = NAME
var name = System.IO.File.ReadAllText("Temp/strshot.txt").Trim();
var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
if (Protobot.Motion.Sandbox.Instance != null && Protobot.Motion.Sandbox.Instance.View != Protobot.Motion.Sandbox.CameraMode.Free) Protobot.Motion.Sandbox.Instance.SetCamera(Protobot.Motion.Sandbox.CameraMode.Free);
Protobot.PivotCamera.Main.SetTransform(new Vector3(-2f, 2f, -2f), new Vector3(35f, 30f, 0f), -11f);
pt.GetMethod("Frames").Invoke(null, new object[] { 4 });
pt.GetMethod("Screenshot").Invoke(null, new object[] { name });
pt.GetMethod("Frames").Invoke(null, new object[] { 1 });
return "cam " + Camera.main.transform.position.ToString("F1");
