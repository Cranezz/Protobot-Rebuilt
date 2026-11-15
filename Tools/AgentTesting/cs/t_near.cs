var g = Protobot.Game.SkillsGame.Current;
var sb = new System.Text.StringBuilder("robot " + g.x.ToString("F1") + "," + g.y.ToString("F1") + " heading " + g.heading + " depth " + g.depth.ToString("F1") + " width " + g.width.ToString("F1") + "\n");
foreach (var rb in Protobot.Motion.Sandbox.Instance.Root.GetComponentsInChildren<Rigidbody>()) {
    if (rb.gameObject.layer != Protobot.Motion.Sandbox.ElementLayer) continue;
    var p = g.field.ToField(rb.position);
    if (Vector2.Distance(p, new Vector2(g.x, g.y)) < 14f) sb.AppendLine(rb.name + " at " + p.ToString("F1") + " h " + g.field.HeightOf(rb.position).ToString("F1") + " (" + rb.GetComponent<Protobot.Game.Pin>() + rb.GetComponent<Protobot.Game.Cup>() + ")");
}
return sb.ToString();
