var g = Protobot.Game.SkillsGame.Current;
g.SetAlliance(false);
g.StartRun();
g.countdown = 0.2f;
UnityEditor.EditorApplication.isPaused = false;
var sb = new System.Text.StringBuilder("loads: ");
foreach (var kv in g.matchLoads) sb.Append(kv.Key + "=" + kv.Value + " ");
return sb.ToString();
