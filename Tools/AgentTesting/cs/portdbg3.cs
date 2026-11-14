var sb = new System.Text.StringBuilder();
var m = typeof(Protobot.Motion.VexHoleFixes).GetMethod("Ports", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
var fixType = typeof(Protobot.Motion.VexHoleFixes).GetNestedType("Fix", System.Reflection.BindingFlags.NonPublic);
foreach (var id in new[] { "VXPNEUMATICS-V5 Pneumatic Air Reservoir", "VXPNEUMATICS-V5 Pneumatic Air Reservoir#", "VXPNEUMATICS-Legacy Pneumatic Air Reservoir", "VXPNEUMATICS-Legacy Pneumatic Air Reservoir#" }) {
  var part = Protobot.PartsManager.GeneratePart(id.TrimEnd((char)35), new Vector3(500f, 500f, 500f), Quaternion.identity);
  var mesh = part.GetComponent<MeshFilter>().sharedMesh;
  var fix = System.Activator.CreateInstance(fixType, true);
  m.Invoke(null, new object[] { mesh, 0.15f, 0.6f, fix, !id.EndsWith("#") });
  var add = (System.Collections.IList)fixType.GetField("add").GetValue(fix);
  sb.Append(id + " mesh " + mesh.name + " readable " + mesh.isReadable + " bounds " + mesh.bounds.size.ToString("F2") + " found " + add.Count + "\n");
  foreach (Protobot.Custom.CustomHole h in add) sb.Append("  " + h.center.ToString("F3") + " axis " + h.axis + " size " + h.size + " depth " + h.depth.ToString("F3") + "\n");
  UnityEngine.Object.DestroyImmediate(part);
}
return sb.ToString();
