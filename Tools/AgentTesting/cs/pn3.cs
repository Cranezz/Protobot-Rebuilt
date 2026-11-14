var sb = new System.Text.StringBuilder();
Protobot.PartsManager.DestroyLoadedObjects();
var cyl = Protobot.PartsManager.GeneratePart("PNMT-50mm-Normal", Vector3.zero, Quaternion.identity);
var pc = cyl.GetComponent<Protobot.Motion.PneumaticCylinder>();
var ports = new System.Collections.Generic.List<Protobot.HoleCollider>();
foreach (var h in cyl.GetComponentsInChildren<Protobot.HoleCollider>(true)) if (h.holeType == Protobot.HoleCollider.HoleType.Threaded && h.transform.parent == cyl.transform) ports.Add(h);
sb.Append("ports " + ports.Count + "\n");
foreach (var port in ports) {
  // the fitting as ScrewPlaceDisplacement puts it: on the port's mouth, facing out
  var outward = port.transform.forward; var mouth = port.transform.position + outward * port.transform.lossyScale.z / 2f;
  var f = Protobot.PartsManager.GeneratePart("VXPNEUMATICS-Fitting for Cylinders", mouth, Quaternion.LookRotation(outward));
  sb.Append(" fitting thread " + f.GetComponent<Protobot.Screw>().ScrewLength.ToString("F3") + " dia " + Protobot.ChildPartGenerator.fittingDiameters[f.GetComponent<MeshFilter>().sharedMesh].ToString("F3") + " port " + port.transform.lossyScale.x.ToString("F3") + "\n");
}
foreach (var id in new[] { "VXPNEUMATICS-Cylinder Mount", "VXPNEUMATICS-Cylinder Rod Pivot" }) {
  var tmp = Protobot.PartsManager.GeneratePart(id, new Vector3(50f, 0f, 0f), Quaternion.identity); var mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
  if (Protobot.MountPlaceDisplacement.For(id, mesh, pc, out var d)) { tmp.transform.SetPositionAndRotation(d.translation.Position, d.rotation.Orientation); sb.Append(id + " placed at " + tmp.transform.position.ToString("F3") + "\n"); }
  else sb.Append(id + " no displacement\n");
}
var nut = Protobot.PartsManager.GeneratePart("VXPNEUMATICS-Piston Nut", new Vector3(60f, 0f, 0f), Quaternion.identity);
return sb.ToString();
