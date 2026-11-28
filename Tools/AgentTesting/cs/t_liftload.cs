// (Play) a saved cascade lift. Temp/liftload.txt: "open string|chain" (loads Sample Builds/Cascade Lift (MODE).pbb, steps
// frames so its screws connect), "info" (strings, bands, motors, bodies), "sim", "stop", "drive POWER" (the lift motors as
// their keys would: reversed ones the other way), "step N" then where the carriage and stage 2 are.
var file = System.IO.Path.Combine(Application.dataPath, "../Temp/liftload.txt");
var cmd = System.IO.File.Exists(file) ? System.IO.File.ReadAllText(file).Trim() : "info";
var inv = System.Globalization.CultureInfo.InvariantCulture;
var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
System.Func<string, System.Collections.Generic.List<GameObject>> ById = id => Protobot.PartsManager.FindLoadedObjects().Where(o => o != null && o.GetComponent<Protobot.SavedObject>() != null && o.GetComponent<Protobot.SavedObject>().id == id).ToList();
if (cmd.StartsWith("open ")) {
  var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
  if (Protobot.Motion.Sandbox.Running) Protobot.Motion.Sandbox.Stop();
  Protobot.PartsManager.DestroyLoadedObjects();
  var data = Protobot.Builds.BuildFile.Read(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Sample Builds/Cascade Lift (" + cmd.Substring(5) + ").pbb");
  Protobot.Builds.SceneBuild.GenerateBuild(data);
  for (var i = 0; i < 15; i++) pt.GetMethod("Frames").Invoke(null, new object[] { 10 });
  return "parts " + data.parts.Length;
}
if (cmd == "sim") { Protobot.Motion.Sandbox.Begin(); return "sim " + Protobot.Motion.Sandbox.Running; }
if (cmd == "stop") { Protobot.Motion.Sandbox.Stop(); return "stopped"; }
var motors = UnityEngine.Object.FindObjectsByType<Protobot.Motion.MotorDrive>(FindObjectsSortMode.None);
if (cmd.StartsWith("drive ")) {
  var p = float.Parse(cmd.Substring(6), inv);
  foreach (var m in motors) m.Manual = Protobot.Motion.Dynamics.Token(m.gameObject, "rev") == "1" ? -p : p;
  return "manual " + p + " on " + motors.Length;
}
if (cmd.StartsWith("step ")) { var n = int.Parse(cmd.Substring(5)); for (var i = 0; i < n; i += 10) pt.GetMethod("Frames").Invoke(null, new object[] { 10 }); }
var sb = new System.Text.StringBuilder();
foreach (var m in motors) sb.AppendLine("motor " + Protobot.Motion.Controls.GivenName(m.gameObject) + " rpm " + m.Rpm + " rev " + Protobot.Motion.Dynamics.Token(m.gameObject, "rev") + " hold " + Protobot.Motion.Dynamics.Token(m.gameObject, "hold") + " keys " + Protobot.Motion.Dynamics.Token(m.gameObject, "fkey") + "/" + Protobot.Motion.Dynamics.Token(m.gameObject, "rkey"));
foreach (var l in Protobot.Motion.StringLine.All) if (l != null) sb.AppendLine("line " + l.kind.key + " nodes " + l.nodes.Count + " " + l.Describe().Replace('\n', ' ') + " parts " + string.Join(",", l.nodes.Select(n => n.post.part != null ? n.post.part.name : "-")));
foreach (var b in Protobot.Motion.ElasticBand.All) if (b != null) sb.AppendLine("band " + b.Describe().Replace('\n', ' '));
var carriage = ById("PLTE-19-6").FirstOrDefault();
var members = ById("VXSLIDEASSEMBLYP-Outer Slide Member").OrderBy(o => o.transform.position.z).ToList();
if (Protobot.Motion.Sandbox.Running && carriage != null && members.Count > 2) {
  var m2 = members.Last();
  sb.AppendLine("stage 2 up " + (Protobot.Motion.Sandbox.SimPoint(m2, m2.transform.position).y - m2.transform.position.y).ToString("F2", inv) + " carriage up " + (Protobot.Motion.Sandbox.SimPoint(carriage, carriage.transform.position).y - carriage.transform.position.y).ToString("F2", inv));
  foreach (var s in Protobot.Motion.Sandbox.Strings()) sb.AppendLine(s.line.kind.key + " slack " + s.slack.ToString("F3", inv) + " pull " + s.tension.ToString("F1", inv));
}
return sb.ToString();
