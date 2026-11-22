// (a game open) The red Loaders' parts: names and boxes in the field frame (x, y, height).
var g = Protobot.Game.SkillsGame.Current; if (g == null) return "open a game";
var f = g.field;
var sb = new System.Text.StringBuilder();
foreach (var t in f.GetComponentsInChildren<Transform>(true)) {
    if (!t.name.Contains("Loader") || t.GetComponentsInChildren<Renderer>(true).Length == 0 || t.parent == null || t.parent.name.Contains("Loader")) continue;
    sb.AppendLine("ASSEMBLY " + t.name);
    foreach (var r in t.GetComponentsInChildren<Renderer>(true)) {
        var b = r.bounds;
        var c = f.ToField(b.center);
        sb.AppendLine("  " + r.name + " field (" + c.x.ToString("F1") + ", " + c.y.ToString("F1") + ") h " + f.HeightOf(b.min).ToString("F1") + ".." + f.HeightOf(b.max).ToString("F1") + " size " + b.size.ToString("F1"));
    }
    if (sb.Length > 2500) break;
}
return sb.ToString();
