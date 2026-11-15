var g = Protobot.Game.SkillsGame.Current;
float m = 0; Vector3 s = Vector3.zero;
foreach (var rb in Protobot.Motion.Sandbox.Instance.Root.GetComponentsInChildren<Rigidbody>()) {
    if (rb.gameObject.layer != Protobot.Motion.Sandbox.Layer) continue;
    m += rb.mass; s += rb.mass * rb.worldCenterOfMass;
}
var com = s / m;
var chassis = (Rigidbody)null; foreach (var rb in Protobot.Motion.Sandbox.Instance.Root.GetComponentsInChildren<Rigidbody>()) if (rb.gameObject.layer == Protobot.Motion.Sandbox.Layer && (chassis == null || rb.mass > chassis.mass)) chassis = rb;
return "total " + m.ToString("F2") + " kg, COM field " + g.field.ToField(com).ToString("F2") + " h " + g.field.HeightOf(com).ToString("F2") + "; chassis COM " + g.field.ToField(chassis.worldCenterOfMass).ToString("F2") + " (body origin " + g.field.ToField(chassis.position).ToString("F2") + ")";
