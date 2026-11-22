// (Play) Each smart device's found sockets: where (part space), which way out, and the model's bounds.
var sb = new System.Text.StringBuilder();
foreach (var id in new[] { "MOTR", "MOTR-5.5W", "VXSENSORS-AI Vision Sensor", "VXSENSORS-Distance Sensor", "VXSENSORS-GPS Sensor", "VXSENSORS-Inertial Sensor", "VXSENSORS-Optical Sensor", "VXSENSORS-Rotation Sensor", "VXSENSORS-Vision Sensor", "VXSENSORS-V5 3 Wire Expander", "RDIO" }) {
    var p = Protobot.PartsManager.GeneratePart(id, new Vector3(0f, 5000f, 0f), Quaternion.identity);
    var b = p.GetComponent<MeshFilter>().sharedMesh.bounds;
    sb.Append(id + " bounds " + b.min.ToString("F2") + ".." + b.max.ToString("F2"));
    foreach (var port in Protobot.Motion.WirePorts.Of(p)) sb.Append(" | at " + port.local.ToString("F2") + " out " + port.outLocal.ToString("F2") + " up " + port.upLocal.ToString("F2"));
    sb.AppendLine();
    UnityEngine.Object.Destroy(p);
}
return sb.ToString();
