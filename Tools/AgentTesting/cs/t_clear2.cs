var g = Protobot.Game.SkillsGame.Current;
var t = typeof(Protobot.Game.SkillsGame);
var f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var move = t.GetMethod("MoveAlong", f);
var inside = t.GetMethod("Inside", f);
var edge = t.GetMethod("EdgeDistance", f);
var spots = g.field.PieceSpots();
var sb = new System.Text.StringBuilder();
g.heading = Protobot.Game.SkillsGame.HeadingOf(Protobot.Game.SkillsGame.FromLeft(g.StartQuadrant, Protobot.Game.SkillsGame.Dir(180f)));
foreach (var s in new[] { 36f, 37f, 37.5f, 38f, 38.5f }) {
    move.Invoke(g, new object[] { s });
    var corners = g.Corners();
    var blocked = "";
    foreach (var sp in spots) {
        var ins = (bool)inside.Invoke(null, new object[] { corners, sp.at });
        var d = (float)edge.Invoke(null, new object[] { corners, sp.at });
        if (ins || d < sp.reach + 0.25f) blocked += sp.at.ToString("F1") + (ins ? " in" : " d" + d.ToString("F1")) + "; ";
    }
    var fails = ""; foreach (var c in g.Checks()) if (!c.Item2) fails += c.Item1.Substring(0, 12) + "|";
    sb.AppendLine("slide " + s + " -> " + g.Slide.ToString("F2") + " corners " + string.Join(" ", System.Array.ConvertAll(corners, c => c.ToString("F1"))) + " blocked " + blocked + " fails " + fails);
}
return sb.ToString();
