var sb = new System.Text.StringBuilder();
var list = new[] { "VXWHEELS-Bogie Wheel Single", "VXWHEELS-Bogie Wheel Double", "VXWHEELS-6 Wheel Leg" };
foreach (var id in list) {
  GameObject part = null; try { part = Protobot.PartsManager.GeneratePart(id, new Vector3(300f, 300f, 300f), Quaternion.identity); } catch (System.Exception e) { sb.Append(id + ": ERROR " + e.Message + "\n"); continue; }
  if (part == null) { sb.Append(id + ": no part\n"); continue; }
  var b = part.GetComponent<MeshFilter>() ? part.GetComponent<MeshFilter>().sharedMesh.bounds : new Bounds();
  var holes = part.GetComponentsInChildren<Protobot.HoleCollider>(true);
  sb.Append(id + ": mesh centre " + b.center.ToString("F2") + " size " + b.size.ToString("F2") + ", " + holes.Length + " holes\n");
  foreach (var h in holes) {
    var t = h.transform;
    sb.Append("   " + part.transform.InverseTransformPoint(t.position).ToString("F3") + " axis " + part.transform.InverseTransformDirection(t.forward).ToString("F1") + " size " + t.lossyScale.x.ToString("F3") + "x" + t.lossyScale.y.ToString("F3") + " depth " + t.lossyScale.z.ToString("F3") + " " + h.holeType + (h.holeData != null && h.holeData.outline != null ? " outline" : "") + "\n");
  }
  UnityEngine.Object.DestroyImmediate(part);
}
System.IO.File.WriteAllText(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/bogie_holes.txt", sb.ToString());
return "ok";
