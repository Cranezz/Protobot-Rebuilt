var g = Protobot.Game.SkillsGame.Current; var sb = new System.Text.StringBuilder();
foreach (var c in g.Checks()) sb.Append((c.ok ? "ok " : "NO ") + c.rule + " | ");
return sb.ToString();
