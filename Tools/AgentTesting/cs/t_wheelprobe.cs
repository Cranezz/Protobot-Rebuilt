// (sandbox running) The tracking wheels' sim bodies: angular speed about their axle, the speed of their middle, the
// contact speed (omega x r), and what the sensors read.
var sb = new System.Text.StringBuilder();
var sandbox = Protobot.Motion.Sandbox.Instance;
foreach (var b in sandbox.Bodies()) {
    var names = string.Join(",", b.nodes.Select(n => n.GetComponent<Protobot.SavedObject>()?.id ?? n.name));
    if (!names.Contains("OMNI-V2-2.75")) continue;
    var rb = b.rb;
    var axis = Vector3.zero;
    foreach (var j in rb.GetComponents<ConfigurableJoint>()) if (j.angularXMotion == ConfigurableJointMotion.Free) { axis = rb.transform.TransformDirection(j.axis).normalized; sb.AppendLine("  own joint target " + j.targetAngularVelocity.x.ToString("F2") + " damper " + j.angularXDrive.positionDamper + " max " + j.angularXDrive.maximumForce.ToString("F3") + " rel w " + (Vector3.Dot(rb.angularVelocity - (j.connectedBody != null ? j.connectedBody.angularVelocity : Vector3.zero), axis)).ToString("F2")); }
    foreach (var o in sandbox.Bodies()) foreach (var j in o.rb.GetComponents<ConfigurableJoint>()) if (j.connectedBody == rb && j.angularXMotion == ConfigurableJointMotion.Free) sb.AppendLine("  other joint target " + j.targetAngularVelocity.x.ToString("F2") + " damper " + j.angularXDrive.positionDamper + " max " + j.angularXDrive.maximumForce.ToString("F3"));
    sb.AppendLine(names + " | mass " + rb.mass.ToString("F3") + " I " + rb.inertiaTensor.ToString("F4") + " w.axis " + Vector3.Dot(rb.angularVelocity, axis).ToString("F2") + " v " + rb.linearVelocity.ToString("F2") + " r*w " + (Vector3.Dot(rb.angularVelocity, axis) * 1.376f).ToString("F2") + " joints " + rb.GetComponents<ConfigurableJoint>().Length + " axis " + axis.ToString("F2"));
}
foreach (var d in Protobot.Devices.V5Brain.Devices.OfType<Protobot.Devices.ShaftSensor>()) sb.AppendLine(Protobot.Motion.Controls.GivenName(d.part) + " rpm " + d.Rpm.ToString("F1") + " -> in/s " + (d.Rpm / 60f * Mathf.PI * 2.75f).ToString("F2"));
return sb.ToString();
