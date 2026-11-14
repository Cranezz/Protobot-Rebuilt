var g = Protobot.Game.SkillsGame.Current;
g.SetAlliance(false);
g.SetQuadrant(Protobot.Game.Quadrant.Top);
g.SetPreloadSpot(Protobot.Game.SkillsGame.PreloadSpot.Left);
g.FrameCamera();
var sb = new System.Text.StringBuilder("x " + g.x + " y " + g.y + " h " + g.heading + "\n");
foreach (var c in g.Checks()) sb.Append((c.ok ? "ok  " : "NO  ") + c.rule + "\n");
return sb.ToString();
