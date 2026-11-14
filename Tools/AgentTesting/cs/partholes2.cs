var sb = new System.Text.StringBuilder();
var list = System.IO.File.ReadAllLines(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/openings_ids.txt"); var unused = new[] {
 "VXSENSORS-GPS Sensor", "VXSENSORS-Line Sensor Mounting Bar", "VXSENSORS-Ultrasonic Range Finder", "VXSENSORS-Optical Shaft Encoder", "VXSENSORS-Line Tracker V2",
 "VXSENSORS-V5 Bumper Switch", "VXSENSORS-Limit Switch", "SWCH-Bumper-V5", "SWCH-Limit-Cortex",
 "VXMOTIONPARTS-1 Post Bearing Flat Standoff Retainer", "VXMOTIONPARTS-1 Post Bearing Flat Hex Retainer", "VXSTRUCTUREPARTS-1 Post Standoff Retainer", "VXSTRUCTUREPARTS-4 Post Standoff Retainer",
 "VXSPACERS-1/8\" Click On Spacer", "VXSLOTTEDANGLE-Slotted Angle", "GSET-Pack-Pivot", "GSET-Pack-Angle", "VXHARDWARE-Coupler", "VXHARDWARE-1/2\" Hex Coupler", "VXHARDWARE-Rubber Link", "VXHINGES-2 Hole Hinge (bottom)", "VXHINGES-2 Hole Hinge (top)", "VXHINGES-2 Hole Hinge Pin", "VXADVANCEDGEARS-Universal Joint", "VXADVANCEDGEARS-Cam Follower", "VXADVANCEDGEARS-Cam Follower Mounting Bracket", "VXSLIDEASSEMBLYP-Inner Acetal Slide Truck", "VXSLIDEASSEMBLYP-Outer Slide Member", "VXPNEUMATICS-Cylinder Mount", "VXPNEUMATICS-Cylinder Rod Pivot", "VXPNEUMATICS-Piston Nut", "VXPNEUMATICS-Fitting for Cylinders", "VXPNEUMATICS-V5 Pneumatic Air Reservoir", "VXPNEUMATICS-Solenoid   Single Acting", "VXGUSSETSVEX-Angle Gusset", "VXGUSSETSVEX-Pivot Gusset", "VXSTRUCTUREPARTS-1 Post Hex Nut Retainer" };
foreach (var id in list) { if (id.Trim().Length == 0) continue;
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
System.IO.File.WriteAllText(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/part_holes.txt", sb.ToString());
return "ok";
