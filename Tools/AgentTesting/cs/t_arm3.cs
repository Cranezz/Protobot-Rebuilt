var g = Protobot.Game.SkillsGame.Current;
var sb = new System.Text.StringBuilder();
foreach (var t in g.Toggles) {
    var p = g.field.ToField(t.transform.position);
    if (p.x > -60f) continue;
    var up = t.faces.OrderByDescending(f => Vector3.Dot(t.transform.TransformDirection(f.normal), Vector3.up)).First();
    sb.Append("left toggle: on top " + up.color + ", set " + t.SetColor + ", touched " + t.TouchedByRobot + ", spin " + t.body.angularVelocity.magnitude.ToString("F2") + "; ");
}
// The arm: the robot body whose top is highest.
foreach (var rb in Protobot.Motion.Sandbox.Instance.Root.GetComponentsInChildren<Rigidbody>()) {
    if (rb.gameObject.layer != Protobot.Motion.Sandbox.Layer) continue;
    var b = new Bounds(rb.position, Vector3.zero); foreach (var c in rb.GetComponentsInChildren<Renderer>()) b.Encapsulate(c.bounds);
    if (g.field.HeightOf(b.max) > 7f) sb.Append("body " + rb.mass.ToString("F2") + " top " + g.field.HeightOf(b.max).ToString("F1") + " at " + g.field.ToField(b.center).ToString("F1") + " min x " + g.field.ToField(b.min).x.ToString("F1") + "; ");
}
return sb.ToString();
