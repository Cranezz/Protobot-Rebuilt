var g = Protobot.Game.SkillsGame.Current;
var f = g.field;
var stock = (System.Collections.Generic.List<UnityEngine.GameObject>)typeof(Protobot.Game.GameField).GetField("stock", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(f);
var names = new System.Collections.Generic.HashSet<string>();
foreach (var s in stock) names.Add(s.name.Length > 24 ? s.name.Substring(0, 24) : s.name);
var sb = new System.Text.StringBuilder("stock: " + string.Join(", ", names) + "\nred loaders " + f.loaders.Count + " blue " + f.blueLoaders.Count + "\n");
foreach (var red in new[] { true, false }) {
  g.SetAlliance(red);
  foreach (var q in red ? new[] { Protobot.Game.Quadrant.Left, Protobot.Game.Quadrant.Bottom } : new[] { Protobot.Game.Quadrant.Right, Protobot.Game.Quadrant.Top }) {
    g.SetQuadrant(q);
    var bad = new System.Collections.Generic.List<string>();
    foreach (var c in g.Checks()) if (!c.ok) bad.Add(c.rule.Substring(0, System.Math.Min(30, c.rule.Length)));
    sb.Append(q + ": x " + g.x.ToString("0.0") + " y " + g.y.ToString("0.0") + " h " + g.heading + " failing: " + string.Join(" | ", bad) + "\n");
  }
}
return sb.ToString();
