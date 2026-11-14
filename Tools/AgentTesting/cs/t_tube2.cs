var sb = new System.Text.StringBuilder();
var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
var point = pt.GetMethod("PointAt", new[] { typeof(Vector3) });
GameObject f1 = null, f2 = null, chan = null;
foreach (var s0 in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None)) {
  if (s0.id == "VXPNEUMATICS-Straight Pneumatic Fitting") f1 = s0.gameObject;
  if (s0.id == "VXPNEUMATICS-Elbow Fitting") f2 = s0.gameObject;
  if (s0.id != null && s0.id.StartsWith("CCHL")) chan = s0.gameObject;
}
Protobot.Motion.TubeTool.Begin(Protobot.Motion.TubeTool.PortNear(f1, f1.transform.position));
var tool = UnityEngine.Object.FindFirstObjectByType<Protobot.Motion.TubeTool>();
var place = typeof(Protobot.Motion.TubeTool).GetMethod("Place", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var holes = chan.GetComponentsInChildren<Protobot.HoleCollider>();
var hole = holes[holes.Length / 2];
var targets = new Vector3[] { new Vector3(1f, 1.4f, 1.0f), hole.transform.position, new Vector3(6.5f, 0.47f, 2.2f), Protobot.Motion.TubeTool.PortNear(f2, f2.transform.position).transform.position };
var level = f1.transform.position;
pt.GetMethod("Frames").Invoke(null, new object[] { 2 });
foreach (var w in targets) {
  point.Invoke(null, new object[] { w });
  pt.GetMethod("Frames").Invoke(null, new object[] { 3 });
  var t = Protobot.Motion.TubeTool.Pick(Camera.main, level, null, true);
  sb.Append("target " + t.valid + " kind " + t.kind + " " + t.what + " at " + t.point.ToString("F2") + "\n");
  if (t.valid) { place.Invoke(tool, new object[] { t }); level = t.point; }
}
UnityEditor.EditorApplication.isPaused = false;
foreach (var tube in Protobot.Motion.PneumaticTube.All) sb.Append("tube nodes " + tube.nodes.Count + " length " + tube.Length.ToString("F2") + " kinds " + string.Join(",", System.Linq.Enumerable.Select(tube.nodes, n => n.kind.ToString())) + " | " + Protobot.Motion.Dynamics.Token(tube.gameObject, "tube") + "\n");
sb.Append("tool active " + Protobot.Motion.TubeTool.Active);
return sb.ToString();
