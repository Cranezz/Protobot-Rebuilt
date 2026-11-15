var g = Protobot.Game.SkillsGame.Current;
var sb = new System.Text.StringBuilder("size " + g.depth.ToString("F2") + " x " + g.width.ToString("F2") + " x " + g.height.ToString("F2") + ", legal " + g.Legal + "; at " + g.x.ToString("F1") + "," + g.y.ToString("F1") + "\n");
foreach (var c in g.Checks()) sb.AppendLine((c.Item2 ? "ok  " : "NO  ") + c.Item1);
return sb.ToString();
