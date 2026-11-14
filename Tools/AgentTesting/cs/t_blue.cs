var g = Protobot.Game.SkillsGame.Current;
var ft = g.field.transform;
var sb = new System.Text.StringBuilder();
foreach (var robot in new[] { 0, 1 }) {
  UnityEngine.Vector3 l = UnityEngine.Vector3.zero, r = UnityEngine.Vector3.zero; int nl = 0, nr = 0;
  foreach (var t in UnityEngine.Object.FindObjectsByType<Protobot.Online.NetPart>(UnityEngine.FindObjectsSortMode.None)) {
    if (t.robot != robot || t.GetComponent<Protobot.Motion.MotorDrive>() == null) continue;
    var side = Protobot.Motion.Controls.Side(t.gameObject);
    // where the simulation has it now, and where the part itself is (its start)
    var p = ft.InverseTransformPoint(Protobot.Motion.Sandbox.SimPoint(t.gameObject, t.transform.position));
    if (side == "L") { l += p; nl++; } else if (side == "R") { r += p; nr++; }
  }
  if (nl == 0 || nr == 0) { sb.Append("robot " + robot + ": no L/R motors | "); continue; }
  var d = r / nr - l / nl;
  sb.Append("robot " + robot + " right-left in field (now) " + d.ToString("F2") + " yaw " + (UnityEngine.Mathf.Atan2(d.x, d.z) * UnityEngine.Mathf.Rad2Deg).ToString("F1") + " | ");
  l = r = UnityEngine.Vector3.zero; nl = nr = 0;
  foreach (var t in UnityEngine.Object.FindObjectsByType<Protobot.Online.NetPart>(UnityEngine.FindObjectsSortMode.None)) {
    if (t.robot != robot || t.GetComponent<Protobot.Motion.MotorDrive>() == null) continue;
    var side = Protobot.Motion.Controls.Side(t.gameObject);
    var p = ft.InverseTransformPoint(t.transform.position);
    if (side == "L") { l += p; nl++; } else if (side == "R") { r += p; nr++; }
  }
  d = r / nr - l / nl;
  sb.Append("start yaw " + (UnityEngine.Mathf.Atan2(d.x, d.z) * UnityEngine.Mathf.Rad2Deg).ToString("F1") + " || ");
}
return sb.ToString();
