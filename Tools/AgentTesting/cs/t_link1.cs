var sb = new System.Text.StringBuilder();
Protobot.PartsManager.DestroyLoadedObjects();
var link = Protobot.PartsManager.GeneratePart("VXHARDWARE-Rubber Link", new Vector3(0f, 3f, 0f), Quaternion.LookRotation(Vector3.up));
var rl = link.GetComponent<Protobot.Motion.RubberLinkPart>();
foreach (var high in new[] { true, false }) {
  var st = rl.StudOf(high);
  sb.Append((high ? "A" : "B") + " face " + st.face.ToString("F3") + " out " + st.outward.ToString("F2") + " len " + st.length.ToString("F3") + "\n");
  var probe = st.face + st.outward * 0.1f;
  var (center, outward, h) = rl.Seat(probe, 1f);
  var so = Protobot.PartsManager.GeneratePart("SNDF-1.00in", center, Quaternion.FromToRotation(Vector3.forward, outward));
  so.name = "SO " + (high ? "A" : "B");
  sb.Append("  standoff at " + center.ToString("F3") + "\n");
}
return sb.ToString();
