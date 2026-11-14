var sb = new System.Text.StringBuilder();
foreach (var d in Protobot.Devices.V5Brain.Devices) {
    if (!(d is Protobot.Devices.DistanceSensor) && !(d is Protobot.Devices.OpticalSensor) && !(d is Protobot.Devices.LineTracker) && !(d is Protobot.Devices.LightSensor)) continue;
    var look = Protobot.Devices.SensorFrame.Look(d.part);
    var o = Protobot.Devices.Pose.Point(d.part, look.Item1);
    var dir = Protobot.Devices.Pose.Dir(d.part, look.Item2);
    sb.Append(d.Name + " local o " + look.Item1 + " d " + look.Item2 + " world o " + o + " dir " + dir + " layer " + d.part.layer);
    UnityEngine.RaycastHit hit;
    if (UnityEngine.Physics.Raycast(o + dir * 0.02f, dir, out hit, 100f, Protobot.Devices.Pose.Mask)) sb.Append(" hit " + hit.collider.name + " at " + hit.distance + " layer " + hit.collider.gameObject.layer);
    else sb.Append(" no hit");
    sb.AppendLine();
}
return sb.ToString();
