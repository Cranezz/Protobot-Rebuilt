var g = Protobot.Game.SkillsGame.Current;
var sb = new System.Text.StringBuilder();
foreach (var rb in Protobot.Motion.Sandbox.Instance.Root.GetComponentsInChildren<Rigidbody>()) {
    if (rb.gameObject.layer != Protobot.Motion.Sandbox.Layer) continue;
    var b = new Bounds(rb.position, Vector3.zero); var names = "";
    foreach (var c in rb.GetComponentsInChildren<Renderer>()) { b.Encapsulate(c.bounds); if (names.Length < 80) names += c.name.Split(' ')[0] + ","; }
    sb.AppendLine(rb.mass.ToString("F2") + " kg: h " + g.field.HeightOf(b.min).ToString("F1") + ".." + g.field.HeightOf(b.max).ToString("F1") + " size " + b.size.ToString("F1") + " w " + rb.angularVelocity.magnitude.ToString("F1") + " [" + names + "]");
}
return sb.ToString();
