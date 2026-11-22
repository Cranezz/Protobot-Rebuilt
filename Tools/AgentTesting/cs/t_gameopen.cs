// (Play) Opens the game on the loaded build (Temp/gamemode.txt: Skills/Match/Programming; default Match) and reports
// what the robot import did: parts left out, build game elements, the checklist.
var mode = System.IO.File.Exists("Temp/gamemode.txt") ? System.IO.File.ReadAllText("Temp/gamemode.txt").Trim() : "Match";
if (Protobot.Game.SkillsGame.Current != null) Protobot.Game.SkillsGame.Current.Exit();
var def = Resources.LoadAll<Protobot.Game.GameDefinition>("Games")[0];
Protobot.Game.SkillsGame.Open(def, true, (Protobot.Game.SkillsGame.Mode)System.Enum.Parse(typeof(Protobot.Game.SkillsGame.Mode), mode));
var g = Protobot.Game.SkillsGame.Current;
if (g == null) return "not opened";
var sb = new System.Text.StringBuilder("left out " + g.LeftOutCount + ", build elements " + g.buildElements.Count + ", problem: " + (g.ElementProblem ?? "none") + "\n");
foreach (var c in g.Checks()) sb.AppendLine((c.Item2 ? "  ok  " : "  NO  ") + c.Item1);
return sb.ToString();
