var g = Protobot.Game.SkillsGame.Current;
if (g == null) return "no game";
var sb = new System.Text.StringBuilder("state " + g.state + " time " + g.timeLeft.ToString("0") + " score " + g.score.total + " pins " + g.Pins.Count + " | sandbox " + Protobot.Motion.Sandbox.Running);
var tags = UnityEngine.Object.FindObjectsByType<Protobot.Online.NetPart>(UnityEngine.FindObjectsSortMode.None);
var r0 = 0; var r1 = 0; foreach (var t in tags) { if (t.robot == 0) r0++; else r1++; }
sb.Append(" | tagged robot0 " + r0 + " robot1 " + r1);
return sb.ToString();
