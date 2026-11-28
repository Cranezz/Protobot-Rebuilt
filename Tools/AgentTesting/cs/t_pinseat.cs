// (Play, a game open, before the run) The model's own height of the pins sitting in cups (display transforms), field frame.
var g = Protobot.Game.SkillsGame.Current; var f = g.field; var sb = new System.Text.StringBuilder();
foreach (var t in f.GetComponentsInChildren<Transform>(true)) {
    if (!t.name.StartsWith("276-9250-80x") && !t.name.StartsWith("276-9250-81x")) continue;
    if (!t.gameObject.activeInHierarchy) continue;
    var rs = t.GetComponentsInChildren<MeshRenderer>(); if (rs.Length == 0) continue;
    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
    var p = f.ToField(b.center);
    if (p.x > -60f) continue;
    sb.AppendLine(t.name.Substring(0, 12) + " at (" + p.x.ToString("F1") + ", " + p.y.ToString("F1") + ") mid h " + f.HeightOf(b.center).ToString("F2") + " top " + f.HeightOf(b.max).ToString("F2"));
}
return sb.ToString();
