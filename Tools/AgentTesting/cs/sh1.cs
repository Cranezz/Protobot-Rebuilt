var sb = new System.Text.StringBuilder();
Protobot.PartsManager.DestroyLoadedObjects();
var sgen = Protobot.PartsManager.GetPartType("SNDF").GetComponent<Protobot.PartGenerator>();
sb.Append("standoff options " + string.Join(",", sgen.GetParam1Options()) + "\n");
var sid = "SNDF-" + sgen.GetParam1Options()[2];
var standoff = Protobot.PartsManager.GeneratePart(sid, Vector3.zero, Quaternion.identity);
foreach (var h in standoff.GetComponentsInChildren<Protobot.HoleCollider>()) sb.Append(" standoff hole " + h.transform.position.ToString("F3") + " fwd " + h.transform.forward.ToString("F2") + " depth " + h.transform.lossyScale.z.ToString("F3") + " " + h.holeType + "\n");
var id = "VXHARDWARE-1/2\" Shoulder Screw (0.18\" shoulder)";
var screw = Protobot.PartsManager.GeneratePart(id, new Vector3(5f, 0f, 0f), Quaternion.identity);
var m = screw.GetComponent<MeshFilter>().sharedMesh;
sb.Append("screw tag " + screw.tag + " Screw " + (screw.GetComponent<Protobot.Screw>() != null) + " len " + screw.GetComponent<Protobot.Screw>().ScrewLength.ToString("F3") + " bounds z " + m.bounds.min.z.ToString("F3") + ".." + m.bounds.max.z.ToString("F3") + " shoulder " + Protobot.ChildPartGenerator.ShoulderLength(id) + "\n");
// a face on the standoff's top hole, pointing out of it
var top = standoff.GetComponentsInChildren<Protobot.HoleCollider>()[0];
var faceGo = new GameObject("test face"); var face = faceGo.AddComponent<Protobot.HoleFace>();
var outward = top.transform.forward; var tip = top.transform.position + outward * top.transform.lossyScale.z / 2f;
if (tip.y < top.transform.position.y) { outward = -outward; tip = top.transform.position + outward * top.transform.lossyScale.z / 2f; }
faceGo.transform.position = tip; face.direction = outward;
sb.Append("rise on standoff end " + Protobot.ScrewPlaceDisplacement.ShoulderRise(face, 0.18f, 0.5f).ToString("F3") + "\n");
// a plain 1/16 plate on top: the face is the plate's top
faceGo.transform.position = tip + outward * 0.0625f;
sb.Append("rise from 1/16 above (plate) " + Protobot.ScrewPlaceDisplacement.ShoulderRise(face, 0.18f, 0.5f).ToString("F3") + "\n");
faceGo.transform.position = tip + outward * 0.3f;
sb.Append("rise from 0.3 above " + Protobot.ScrewPlaceDisplacement.ShoulderRise(face, 0.18f, 0.5f).ToString("F3") + "\n");
// put the screw in the standoff with the rise: head underside 0.18 above the end
screw.transform.SetPositionAndRotation(tip + outward * 0.18f, Quaternion.LookRotation(outward));
UnityEngine.Object.Destroy(faceGo);
return sb.ToString();
