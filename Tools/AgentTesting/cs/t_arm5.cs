// The robot 4.5" off the wall under the Toggle, run on; the arm swung back onto the bar (then t_arm6 drives away).
var g = Protobot.Game.SkillsGame.Current;
Protobot.Motion.Sandbox.TestSticks = () => (Vector2.zero, Vector2.zero);
foreach (var m in UnityEngine.Object.FindObjectsByType<Protobot.Motion.MotorDrive>(UnityEngine.FindObjectsSortMode.None)) m.Manual = 0f;
g.BackToSetup();
g.SetPreload(false);
g.SetPose(-61.7f, 0f, 90f);
g.StartRun();
return "ok";
