// Play mode: the washers' model box, middle hole and the axle placing works out (AxlePlaceDisplacement.AxleOf).
var sb = new System.Text.StringBuilder();
foreach (var id in new[] { "VXSPACERS-0.02\" Steel Washer (3/8\" OD)", "VXSPACERS-0.03\" Steel Washer (3/8\" OD)", "VXSPACERS-1/16\" Steel Washer (3/8\" OD)", "VXSPACERS-0.03\" Teflon Washer (3/8\" OD)", "VXSPACERS-0.04\" Teflon Washer (3/8\" OD)", "RSSP-W32" }) {
  UnityEngine.GameObject p = null; try { p = Protobot.PartsManager.GeneratePart(id, new UnityEngine.Vector3(0f, -100f, 0f), UnityEngine.Quaternion.identity); } catch (System.Exception e) { sb.AppendLine(id + ": " + e.Message); continue; }
  var m = p.GetComponent<UnityEngine.MeshFilter>().sharedMesh;
  sb.Append(id + ": bounds c " + m.bounds.center.ToString("0.000") + " size " + m.bounds.size.ToString("0.000") + " verts " + m.vertexCount);
  foreach (var h in p.GetComponentsInChildren<Protobot.HoleCollider>(true)) sb.Append(" | hole at " + h.transform.localPosition.ToString("0.000") + " fwd " + (h.transform.localRotation * UnityEngine.Vector3.forward).ToString("0.00") + " scale " + h.transform.localScale.ToString("0.000"));
  Protobot.AxlePlaceDisplacement.Axle a;
  var ok = Protobot.AxlePlaceDisplacement.AxleOf(p, out a);
  sb.AppendLine(" | axle " + ok + " c " + a.center.ToString("0.000") + " ax " + a.axis.ToString("0.00") + " " + a.low.ToString("0.000") + ".." + a.high.ToString("0.000"));
  UnityEngine.Object.Destroy(p);
}
return sb.ToString();
