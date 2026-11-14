var g = Protobot.Game.SkillsGame.Current;
var cam = Protobot.PivotCamera.Main;
var c = g.field.ToWorld(UnityEngine.Vector2.zero, 0f);
var sb = Protobot.Motion.Sandbox.Instance;
if (sb != null) sb.SetCamera(Protobot.Motion.Sandbox.CameraMode.Free);
cam.SetTransform(c, new UnityEngine.Vector3(70f, g.field.transform.eulerAngles.y, 0f), -260f);
return "top";
