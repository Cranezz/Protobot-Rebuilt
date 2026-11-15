// Joints of the robot's light bodies (rotors): what they connect to and where.
var g = Protobot.Game.SkillsGame.Current;
var sb = new System.Text.StringBuilder();
foreach (var rb in Protobot.Motion.Sandbox.Instance.Root.GetComponentsInChildren<Rigidbody>()) {
    if (rb.gameObject.layer != Protobot.Motion.Sandbox.Layer) continue;
    var joints = rb.GetComponents<Joint>();
    var b = new Bounds(rb.position, Vector3.zero); foreach (var c in rb.GetComponentsInChildren<Collider>()) b.Encapsulate(c.bounds);
    sb.Append("body " + rb.mass.ToString("F2") + " kg at " + g.field.ToField(rb.position).ToString("F1") + " h " + g.field.HeightOf(rb.position).ToString("F2") + " low " + g.field.HeightOf(b.min).ToString("F2") + " kin " + rb.isKinematic + ": ");
    foreach (var j in joints) sb.Append(j.GetType().Name + "->" + (j.connectedBody != null ? j.connectedBody.mass.ToString("F2") : "world") + " ");
    sb.AppendLine();
}
return sb.ToString();
