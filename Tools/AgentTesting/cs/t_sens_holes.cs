var sb = new System.Text.StringBuilder();
foreach (var fam in new[] { "VXSENSORS", "VXV5ELECTRONICS" }) {
  var type = Protobot.PartsManager.GetPartType(fam);
  if (type == null) { sb.Append(fam + " missing\n"); continue; }
  foreach (var p1 in type.GetComponent<Protobot.PartGenerator>().GetParam1Options()) {
    if (fam == "VXV5ELECTRONICS" && !(p1.Contains("Sensor") || p1.Contains("Vision") || p1.Contains("GPS") || p1.Contains("Inertial") || p1.Contains("Rotation") || p1.Contains("Distance") || p1.Contains("Optical") || p1.Contains("Bumper") || p1.Contains("Camera"))) continue;
    UnityEngine.GameObject go = null;
    try { go = Protobot.PartsManager.GeneratePart(fam + "-" + p1, new UnityEngine.Vector3(0, -500, 0), UnityEngine.Quaternion.identity); } catch (System.Exception e) { sb.Append(p1 + ": ERR " + e.Message + "\n"); continue; }
    var holes = go.GetComponentsInChildren<Protobot.HoleCollider>(true);
    var b = go.GetComponent<UnityEngine.MeshFilter>().sharedMesh.bounds;
    sb.Append(fam.Substring(2,3) + " | " + p1 + " | size " + b.size.ToString("F2") + " | holes " + holes.Length + ": ");
    foreach (var h in holes) { var s = h.transform.localScale; sb.Append(s.x.ToString("F3") + "x" + s.y.ToString("F3") + "/" + s.z.ToString("F2") + h.holeType.ToString().Substring(0,1) + " "); }
    sb.Append("\n");
    UnityEngine.Object.Destroy(go);
  }
}
return sb.ToString();
