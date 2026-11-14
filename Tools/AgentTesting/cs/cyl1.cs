Protobot.PartsManager.DestroyLoadedObjects();
var cyl = Protobot.PartsManager.GeneratePart("PNMT-50mm-Normal", Vector3.zero, Quaternion.identity);
var pc = cyl.GetComponent<Protobot.Motion.PneumaticCylinder>();
var tmp = Protobot.PartsManager.GeneratePart("VXPNEUMATICS-Cylinder Mount", new Vector3(50f, 0f, 0f), Quaternion.identity);
Protobot.MountPlaceDisplacement.For("VXPNEUMATICS-Cylinder Mount", tmp.GetComponent<MeshFilter>().sharedMesh, pc, out var d);
tmp.transform.SetPositionAndRotation(d.translation.Position, d.rotation.Orientation);
// a nut on the thread just past the mount's base
var t = pc.Thread.transform; var rot = Quaternion.LookRotation(Vector3.up, t.forward); var nut = Protobot.PartsManager.GeneratePart("VXPNEUMATICS-Piston Nut", Vector3.zero, rot); var hl = nut.GetComponentInChildren<Protobot.HoleCollider>().transform.localPosition; nut.transform.position = t.position - t.forward * 0.12f - rot * hl;
return "made; thread fwd " + t.forward.ToString("F2");
