var g = Protobot.Game.SkillsGame.Current;
Rigidbody chassis = null; foreach (var rb in Protobot.Motion.Sandbox.Instance.Root.GetComponentsInChildren<Rigidbody>()) if (rb.gameObject.layer == Protobot.Motion.Sandbox.Layer && (chassis == null || rb.mass > chassis.mass)) chassis = rb;
var up = g.field.transform.InverseTransformDirection(chassis.transform.up);
var sb = new System.Text.StringBuilder("chassis up in field frame " + up.ToString("F3") + " tilt " + Vector3.Angle(up, Vector3.up).ToString("F1") + "°; v " + chassis.linearVelocity.magnitude.ToString("F2") + " w " + chassis.angularVelocity.magnitude.ToString("F2") + "\n");
foreach (var j in chassis.GetComponents<ConfigurableJoint>()) {
    var anchorWorld = chassis.transform.TransformPoint(j.anchor);
    var other = j.connectedBody;
    var otherAnchor = other.transform.TransformPoint(j.connectedAnchor);
    sb.AppendLine("wheel joint anchor gap " + (anchorWorld - otherAnchor).magnitude.ToString("F3") + " wheel h " + g.field.HeightOf(other.position).ToString("F2"));
}
return sb.ToString();
