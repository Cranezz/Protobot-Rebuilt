var g = Protobot.Game.SkillsGame.Current;
var r = "paused " + UnityEditor.EditorApplication.isPaused + " state " + g.state + " time " + g.timeLeft.ToString("0.0") + " auto " + g.InAutonomous + " drive " + Protobot.Motion.Sandbox.DriveEnabled;
if (g.state == Protobot.Game.SkillsGame.State.Running && g.timeLeft > 106f) g.timeLeft = 105.4f;
return r;
