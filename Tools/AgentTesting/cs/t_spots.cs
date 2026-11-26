// (Play, a match setup open) Temp/spots.txt: "list" the legal spots; "go N" puts the robot on spot N; "snap V" slide V
// snapped; "aim" aims at the nearest Goal; "turn T"; "quad Q"; always reports where it is and whether it's legal.
var cmd = System.IO.File.ReadAllText("Temp/spots.txt").Trim().Split(' ');
var g = Protobot.Game.SkillsGame.Current; if (g == null) return "no game";
var sb = new System.Text.StringBuilder();
var spots = g.Spots();
if (cmd[0] == "list") for (var i = 0; i < spots.Count; i++) sb.AppendLine(i + ": " + spots[i].what + " (slide " + spots[i].slide.ToString("F2") + ")");
if (cmd[0] == "go") g.SetSlide(spots[int.Parse(cmd[1])].slide);
if (cmd[0] == "snap") { var v = g.SnapSlide(float.Parse(cmd[1]), out var w); sb.AppendLine("snap " + cmd[1] + " -> " + v.ToString("F2") + " " + w); g.SetSlide(v); }
if (cmd[0] == "turn") g.Face(float.Parse(cmd[1]));
if (cmd[0] == "quad") g.SetQuadrant((Protobot.Game.Quadrant)System.Enum.Parse(typeof(Protobot.Game.Quadrant), cmd[1]));
if (cmd[0] == "aim") {
  var me = new Vector2(g.x, g.y); var best = Vector2.zero; var bd = 1e9f;
  foreach (var goal in g.field.goals) { var d = Vector2.Distance(goal.position, me); if (d < bd) { bd = d; best = goal.position; } }
  g.AimAt(best);
  var f = Protobot.Game.SkillsGame.Dir(g.heading); var to = (best - new Vector2(g.x, g.y)).normalized;
  sb.AppendLine("aimed at goal " + best + " off by " + Vector2.Angle(f, to).ToString("F1") + " deg");
}
var corners = g.Corners();
sb.Append("quadrant " + g.StartQuadrant + " slide " + g.Slide.ToString("F2") + " turn " + g.Turn.ToString("F1") + " spot " + (g.SpotNow() ?? "-") + " legal " + g.Legal);
foreach (var c in g.Checks()) if (!c.ok) sb.Append(" | NO: " + c.rule);
return sb.ToString();
