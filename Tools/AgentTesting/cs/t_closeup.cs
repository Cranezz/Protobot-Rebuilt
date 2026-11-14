var g = Protobot.Game.SkillsGame.Current;
var cam = Protobot.PivotCamera.Main;
cam.SetTransform(g.field.ToWorld(new UnityEngine.Vector2(-68f, 66f), 2f), new UnityEngine.Vector3(28f, g.field.transform.rotation.eulerAngles.y + 135f, 0f), -34f);
return "close";
