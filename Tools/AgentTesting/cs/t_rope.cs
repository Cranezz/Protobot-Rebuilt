// Temp/rope.txt: "info", or "push dx dy dz" (moves the second fitting's body in the simulation by that much).
var arg = System.IO.File.ReadAllText("Temp/rope.txt").Trim().Split(' ');
var tube = Protobot.Motion.PneumaticTube.All[0];
GameObject f2 = null;
foreach (var s0 in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None))
  if (s0.id == "VXPNEUMATICS-Elbow Fitting") f2 = s0.gameObject;
var sb = new System.Text.StringBuilder();
var sim = Protobot.Motion.Sandbox.Instance;
if (arg[0] == "push" && sim != null) {
  var field = typeof(Protobot.Motion.Sandbox).GetField("bodyOf", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
  var dict = (System.Collections.IDictionary)field.GetValue(sim);
  var body = dict[f2];
  var rb = (Rigidbody)body.GetType().GetField("rb").GetValue(body);
  rb.isKinematic = true;
  var start = (Matrix4x4)body.GetType().GetField("startInverse").GetValue(body);
  var startPose = start.inverse;
  rb.rotation = startPose.rotation;
  rb.position = (Vector3)startPose.GetColumn(3) + new Vector3(float.Parse(arg[1]), float.Parse(arg[2]), float.Parse(arg[3]));
  rb.transform.SetPositionAndRotation(rb.position, rb.rotation);
}
var line = tube.Line;
float len = 0f, most = 0f;
for (int i = 0; i + 1 < line.Count; i++) { var d = Vector3.Distance(line[i], line[i + 1]); len += d; most = Mathf.Max(most, d); }
int inside = 0; string where = "";
sb.Append("running " + Protobot.Motion.Sandbox.Running + " points " + line.Count + " drawn " + len.ToString("F2") + " rest " + tube.Length.ToString("F2") + " longest seg " + most.ToString("F3") + " inside " + inside + " " + where);
if (f2 != null) sb.Append(" f2 sim " + Protobot.Motion.Sandbox.SimPoint(f2, f2.transform.position).ToString("F2"));
return sb.ToString();
