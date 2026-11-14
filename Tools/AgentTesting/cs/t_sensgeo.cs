var sb = new System.Text.StringBuilder();
var ids = new[] { "SNSR-Potentiometer-V5", "SNSR-Distance-V5", "SNSR-Distance-Cortex", "SNSR-Rotation-V5", "SNSR-Rotation-Cortex", "SNSR-Optical-V5", "SNSR-Optical-Cortex",
    "SNSR-Vision-V5", "SNSR-Inertial-V5", "SNSR-Line Tracker-Cortex", "SWCH-Bumper-V5", "SWCH-Bumper-Cortex", "SWCH-Limit-Cortex",
    "VXSENSORS-Light Sensor", "VXSENSORS-Line Tracker V2", "VXSENSORS-Optical Shaft Encoder", "VXSENSORS-Ultrasonic Range Finder", "VXSENSORS-Potentiometer V2",
    "VXSENSORS-Analog Accelerometer", "VXSENSORS-Yaw Rate Gyroscope Sensor", "VXSENSORS-GPS Sensor", "VXPNEUMATICS-Solenoid   Double Acting" };
System.Func<UnityEngine.Vector3, string> F = v => "(" + v.x.ToString("0.###") + "," + v.y.ToString("0.###") + "," + v.z.ToString("0.###") + ")";
var x = 0f;
foreach (var id in ids) {
    UnityEngine.GameObject part = null;
    try { part = Protobot.PartsManager.GeneratePart(id, new UnityEngine.Vector3(x, 30f, 0f), UnityEngine.Quaternion.identity); } catch (System.Exception e) { sb.AppendLine(id + " FAIL " + e.Message); continue; }
    x += 8f;
    if (part == null) { sb.AppendLine(id + " null"); continue; }
    var mf = part.GetComponent<UnityEngine.MeshFilter>();
    var b = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new UnityEngine.Bounds();
    sb.AppendLine(id + " name=" + part.name + " meshBounds c" + F(b.center) + " s" + F(b.size) + " subs " + (mf != null && mf.sharedMesh != null ? mf.sharedMesh.subMeshCount : 0));
    foreach (var p in Protobot.Motion.WirePorts.Of(part)) sb.AppendLine("   port " + F(p.local) + " out" + F(p.outLocal) + " up" + F(p.upLocal));
    foreach (var h in part.GetComponentsInChildren<Protobot.HoleCollider>(true)) {
        var t = h.transform;
        sb.AppendLine("   hole " + h.holeType + " at" + F(part.transform.InverseTransformPoint(t.position)) + " fwd" + F(part.transform.InverseTransformDirection(t.forward)) + " size" + h.holeData.size + " depth " + h.holeData.depth.ToString("0.###") + " shape " + (h.holeData.shape != null ? h.holeData.shape.name : "-"));
    }
    var mr = part.GetComponent<UnityEngine.MeshRenderer>();
    if (mr != null) { sb.Append("   mats "); foreach (var m in mr.sharedMaterials) sb.Append((m != null ? m.name : "null") + "; "); sb.AppendLine(); }
}
return sb.ToString();
