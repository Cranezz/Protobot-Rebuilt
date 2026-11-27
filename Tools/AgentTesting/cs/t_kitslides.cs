// Play mode: an outer slide member along x at (0, 2, 40) with VEX's truck, the classic inner trunk and the classic
// outer trunk seated on it (SlideTruck.Seat); a picture.
foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None)) UnityEngine.Object.Destroy(s.gameObject);
var m = Protobot.PartsManager.GeneratePart("VXSLIDEASSEMBLYP-Outer Slide Member", new UnityEngine.Vector3(0f, 2f, 40f), UnityEngine.Quaternion.identity);
var sb = new System.Text.StringBuilder();
var xs = new[] { -1.5f, 1.5f, 4.5f };
var ids = new[] { Protobot.Motion.SlideTruck.TruckId, Protobot.Motion.SlideTruck.ClassicInner, Protobot.Motion.SlideTruck.ClassicOuter };
for (var i = 0; i < 3; i++) {
  var t = Protobot.PartsManager.GeneratePart(ids[i], new UnityEngine.Vector3(0f, -50f, 0f), UnityEngine.Quaternion.identity);
  var d = Protobot.Motion.SlideTruck.Seat(m, m.transform.TransformPoint(new UnityEngine.Vector3(xs[i], 0f, 0f)), true, ids[i], t.GetComponent<UnityEngine.MeshFilter>().sharedMesh.bounds);
  t.transform.SetPositionAndRotation(d.translation.Position, d.rotation.Orientation);
  sb.AppendLine(ids[i] + " at " + t.transform.position.ToString("0.00"));
}
var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
pt.GetMethod("Frames").Invoke(null, new object[] { 3 });
foreach (var tr in UnityEngine.Object.FindObjectsByType<Protobot.Motion.SlideTruck>(UnityEngine.FindObjectsSortMode.None)) { tr.Check(); sb.AppendLine(tr.name + " member " + (tr.Member != null)); }
return sb.ToString();
