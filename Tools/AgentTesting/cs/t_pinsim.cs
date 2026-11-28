// (Play, a run going) The simulated pins' drawn middles near the red wall (field frame), to compare with t_pinseat.
var g = Protobot.Game.SkillsGame.Current; var f = g.field; var sb = new System.Text.StringBuilder();
foreach (var pin in g.Pins) {
    var rs = pin.GetComponentsInChildren<MeshRenderer>(); if (rs.Length == 0) continue;
    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
    var p = f.ToField(b.center); if (p.x > -60f) continue;
    sb.AppendLine("sim pin at (" + p.x.ToString("F1") + ", " + p.y.ToString("F1") + ") mid h " + f.HeightOf(b.center).ToString("F2") + " top " + f.HeightOf(b.max).ToString("F2"));
}
return sb.ToString();
