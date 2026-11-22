// (Play) Trailer: the match score reveal from the current game as it stands (match scoring: red and blue), shown the
// way a finished online match shows it, without posting anything anywhere. Sets the run to Results first.
var g = Protobot.Game.SkillsGame.Current;
if (g == null) return "no game";
var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var pins = (System.Collections.Generic.List<Protobot.Game.Pin>)typeof(Protobot.Game.SkillsGame).GetField("pins", bf).GetValue(g);
var cups = (System.Collections.Generic.List<Protobot.Game.Cup>)typeof(Protobot.Game.SkillsGame).GetField("cups", bf).GetValue(g);
var toggles = (System.Collections.Generic.List<Protobot.Game.Toggle>)typeof(Protobot.Game.SkillsGame).GetField("toggles", bf).GetValue(g);
var score = Protobot.Game.OverrideScoring.Evaluate(g.field, pins, cups, toggles, false, true, 0, 0, true);
g.score = score;
typeof(Protobot.Game.SkillsGame).GetField("state", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(g, Protobot.Game.SkillsGame.State.Results);
Protobot.Motion.Sandbox.DriveEnabled = false;
Protobot.Game.ScoreReveal.Play(g, false);
return "reveal: red " + score.red + " blue " + score.blue + ", items " + score.items.Count;
