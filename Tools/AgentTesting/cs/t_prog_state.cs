var g = Protobot.Game.SkillsGame.Current;
var p = Protobot.RobotCode.RobotProgram.Current;
return (g == null ? "no game" : g.ModeName + " state=" + g.state + " t=" + g.timeLeft.ToString("0.0") + " drive=" + Protobot.Motion.Sandbox.DriveEnabled + " score=" + g.score.total)
  + " program=" + (p == null ? "none" : "running") + " marks=" + UnityEngine.PlayerPrefs.GetString("t_code_marks", "");
