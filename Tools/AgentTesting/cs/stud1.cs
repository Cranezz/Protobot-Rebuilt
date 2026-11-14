var sb = new System.Text.StringBuilder();
Protobot.PartsManager.DestroyLoadedObjects();
var a = Protobot.PartsManager.GeneratePart("SNDF-3/4in", Vector3.zero, Quaternion.identity);
var b = Protobot.PartsManager.GeneratePart("SNDF-3/4in", new Vector3(0f, 0f, 0.75f), Quaternion.identity);
// the coupler: tip 1/4" into a's top end (z = 0.375), the rest up into b
var c = Protobot.PartsManager.GeneratePart("VXHARDWARE-Coupler", new Vector3(0f, 0f, 0.125f), Quaternion.LookRotation(Vector3.back));
var s = c.GetComponent<Protobot.Screw>();
sb.Append("coupler: tag " + c.tag + " screw " + (s != null) + " len " + s.ScrewLength.ToString("F3") + " holes " + c.GetComponentsInChildren<Protobot.HoleCollider>().Length + "\n");
// rubber link: stud into a's bottom end (z = -0.375), rubber hanging below
var l = Protobot.PartsManager.GeneratePart("VXHARDWARE-Rubber Link", new Vector3(0f, 0f, -0.375f + 0.365f), Quaternion.LookRotation(Vector3.forward));
var lm = l.GetComponent<MeshFilter>().sharedMesh;
sb.Append("link: len " + l.GetComponent<Protobot.Screw>().ScrewLength.ToString("F3") + " mesh z " + lm.bounds.min.z.ToString("F3") + ".." + lm.bounds.max.z.ToString("F3") + "\n");
// shoulder screw head-down into a 2" standoff, and the free spot for a standoff on it
var sh = Protobot.PartsManager.GeneratePart("VXHARDWARE-1.00\" Shoulder Screw (0.12\" shoulder)", new Vector3(5f, 0f, 0f), Quaternion.identity);
var ss = sh.GetComponent<Protobot.Screw>();
var spot = ss.FreeSpot(sh.transform.position - sh.transform.forward * 0.05f, 0.25f, true);
var spot2 = ss.FreeSpot(sh.transform.position - sh.transform.forward * 0.05f, 0.25f, false);
sb.Append("shoulder " + ss.Shoulder + " threaded part starts " + Vector3.Distance(spot, sh.transform.position).ToString("F3") + " below the head; plain part " + Vector3.Distance(spot2, sh.transform.position).ToString("F3") + "\n");
return sb.ToString();
