var g = Protobot.Game.SkillsGame.Current;
var t = typeof(Protobot.Game.SkillsGame);
var f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var move = t.GetMethod("MoveAlong", f);
var spots = g.field.PieceSpots();
var sb = new System.Text.StringBuilder("pieces near the wall: ");
foreach (var s in spots) if (s.at.x < -55f) sb.Append(s.at.ToString("F1") + " r" + s.reach.ToString("F1") + "; ");
sb.AppendLine();
float kx = g.x, ky = g.y, kh = g.heading;
foreach (var turn in new[] { 90f, -90f }) {
    g.heading = Protobot.Game.SkillsGame.HeadingOf(Protobot.Game.SkillsGame.FromLeft(g.StartQuadrant, Protobot.Game.SkillsGame.Dir(90f + turn)));
    for (var s = -44f; s <= 44f; s += 4f) {
        move.Invoke(g, new object[] { s });
        var fails = new System.Text.StringBuilder();
        foreach (var c in g.Checks()) if (!c.Item2) fails.Append(c.Item1.Substring(0, 12) + "|");
        sb.AppendLine("turn " + turn + " slide " + s + " -> " + g.Slide.ToString("F1") + " fails " + fails);
    }
}
g.x = kx; g.y = ky; g.heading = kh;
return sb.ToString();
