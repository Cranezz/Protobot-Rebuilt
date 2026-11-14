var sb = new System.Text.StringBuilder();
Protobot.PartsManager.DestroyLoadedObjects();
var type = Protobot.PartsManager.GetPartType("VXPNEUMATICS");
var gen = type.GetComponent<Protobot.PartGenerator>();
var x = 0f;
foreach (var opt in gen.GetParam1Options()) {
  if (!(opt.Contains("Fitting") || opt.Contains("Tee") || opt.Contains("Plug") || opt.Contains("Valve") || opt.Contains("Solenoid") || opt.Contains("Reservoir") || opt.Contains("Regulator"))) continue;
  gen.param1.value = opt;
  var o = gen.Generate(new Vector3(x, 2f, 0f), Quaternion.identity);
  x += 2f;
  var screw = o.GetComponent<Protobot.Screw>();
  var mf = o.GetComponent<MeshFilter>();
  sb.Append(opt + " id " + o.GetComponent<Protobot.SavedObject>()?.id + " screw " + (screw != null) + " bounds c " + mf.sharedMesh.bounds.center.ToString("F2") + " s " + mf.sharedMesh.bounds.size.ToString("F2") + " fwd " + o.transform.forward.ToString("F1") + "\n");
  foreach (var h in o.GetComponentsInChildren<Protobot.HoleCollider>(true)) {
    var t = h.transform;
    sb.Append("   hole " + t.name + " local " + o.transform.InverseTransformPoint(t.position).ToString("F3") + " axis " + o.transform.InverseTransformDirection(t.forward).ToString("F2") + " scale " + t.localScale.ToString("F3") + " type " + h.holeType.ToString() + "\n");
  }
}
return sb.ToString();
