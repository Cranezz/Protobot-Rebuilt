// (Play) the loaded build's motors by name and level, and each motor part's turn: Temp/arm.txt "list" | "run NAME POWER" | "sim" | "stop"
var cmd = System.IO.File.ReadAllText("Temp/arm.txt").Trim();
var inv = System.Globalization.CultureInfo.InvariantCulture;
if (cmd == "sim") { Protobot.Motion.Sandbox.Begin(); return "sim " + Protobot.Motion.Sandbox.Running; }
if (cmd == "stop") { Protobot.Motion.Sandbox.Stop(); return "stopped"; }
if (cmd.StartsWith("run ")) {
  var f = cmd.Split(' ');
  var m = System.Linq.Enumerable.FirstOrDefault(Protobot.Motion.MotorDrive.Live(), x => Protobot.Motion.Controls.GivenName(x.gameObject) == f[1].Replace('_', ' '));
  if (m == null) return "no motor " + f[1];
  m.Manual = float.Parse(f[2], inv);
  return "manual " + m.Manual;
}
var sb = new System.Text.StringBuilder();
foreach (var m in Protobot.Motion.MotorDrive.Live()) {
  var rb = Protobot.Motion.Sandbox.BodyOf(m.gameObject);
  sb.AppendLine(Protobot.Motion.Controls.GivenName(m.gameObject) + " level " + m.Level.ToString("F2", inv) + " cmd " + m.Command.ToString("F2", inv));
}
foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None)) {
  if (s.id == null || !s.id.StartsWith("CCHL") ) continue;
  var p = Protobot.Motion.Sandbox.SimPoint(s.gameObject, s.GetComponent<Renderer>().bounds.center);
  var c0 = s.GetComponent<Renderer>().bounds.center; if (s.name == "Aluminum Part (24)" || (p - c0).magnitude > 1.5f) sb.AppendLine("channel " + s.name + " at " + p.ToString("F2") + " (built " + c0.ToString("F2") + ")");
}
sb.AppendLine("t " + Time.time.ToString("F2", inv));
return sb.ToString();
