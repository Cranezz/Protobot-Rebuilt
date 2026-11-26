// (Play) station 1: slack vs arm turn, and why a path fails
var c = Protobot.Motion.ChainLoop.All.First(x => x != null && x.transform.position.x < 0);
var t = Protobot.Motion.ChainTensioner.Of(c);
var sb = new System.Text.StringBuilder();
var scratch = new System.Collections.Generic.List<Protobot.Motion.ChainWrap>();
var inv = System.Globalization.CultureInfo.InvariantCulture;
for (var a = -40f; a <= 40f; a += 4f) {
    var f = Protobot.Motion.ChainTensioner.Slack(c, c.wraps, t.wraps, i => c.transform.TransformPoint(c.wraps[i].center), t.Point, t.Axis, a, scratch);
    var p = Protobot.Motion.ChainPath.Build(scratch, c.family.pitch, Vector2.down, c.fixedLinks);
    sb.AppendLine(a.ToString("F0", inv) + ": " + f.ToString("F3", inv) + " idler " + scratch[2].center.ToString("F2") + (p.Valid ? "" : " " + p.problem));
}
return "pull " + t.pull + "\n" + sb;
