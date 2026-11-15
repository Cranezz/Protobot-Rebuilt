// Robot's back to the left wall under its Toggle, run started (practice: that start isn't legal), arm motor on.
var g = Protobot.Game.SkillsGame.Current;
Protobot.Motion.Sandbox.TestSticks = () => (Vector2.zero, Vector2.zero);
foreach (var m in UnityEngine.Object.FindObjectsByType<Protobot.Motion.MotorDrive>(UnityEngine.FindObjectsSortMode.None)) m.Manual = 0f;
g.BackToSetup();
g.SetPreload(false);
g.SetPose(-60f, 0f, 90f);
g.StartRun();
return "at " + g.x.ToString("F1") + "," + g.y.ToString("F1") + " heading " + g.heading;
