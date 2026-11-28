// (Play) the cascade lift samples (Editor/CascadeLiftRig). Temp/lift.txt: "build MODE" (string|chain: clears, builds,
// steps frames so the screws connect), "finish MODE [BANDS] [TRAVEL]", "sim", "stop", "drive POWER" (both motors,
// -1..1; the left one mirrored when it's reversed), "step N" (stepped frames, then read), "read" (stage 2 and the
// carriage: how high they are over where they started, in the simulation; each string/chain: slack, pull),
// "save PATH", "info" (the strings and bands as built).
var file = System.IO.Path.Combine(Application.dataPath, "../Temp/lift.txt");
var cmd = System.IO.File.Exists(file) ? System.IO.File.ReadAllText(file).Trim() : "read";
var inv = System.Globalization.CultureInfo.InvariantCulture;
var rig = System.Type.GetType("Protobot.EditorTools.CascadeLiftRig, Assembly-CSharp-Editor");
var test = System.Type.GetType("Protobot.EditorTools.MotionTestRig, Assembly-CSharp-Editor");
var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
var P = (System.Collections.Generic.Dictionary<string, GameObject>)rig.GetField("P").GetValue(null);
try {
if (cmd.StartsWith("build")) {
  var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
  if (Protobot.Motion.Sandbox.Running) Protobot.Motion.Sandbox.Stop();
  Protobot.PartsManager.DestroyLoadedObjects();
  test.GetMethod("Clear").Invoke(null, null);
  pt.GetMethod("Frames").Invoke(null, new object[] { 2 });
  var log = (string)rig.GetMethod("Build").Invoke(null, new object[] { cmd.Split(' ')[1] });
  for (var i = 0; i < 12; i++) pt.GetMethod("Frames").Invoke(null, new object[] { 10 });
  return log + "parts " + Protobot.PartsManager.FindLoadedObjects().Count;
}
if (cmd.StartsWith("finish")) {
  var f = cmd.Split(' ');
  var bands = f.Length > 2 ? int.Parse(f[2]) : 2;
  var travel = f.Length > 3 ? float.Parse(f[3], inv) : 5f;
  return (string)rig.GetMethod("Finish").Invoke(null, new object[] { f[1], bands, travel });
}
if (cmd.StartsWith("evenup ")) { typeof(Protobot.Motion.Sandbox).GetField("EvenUp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).SetValue(null, float.Parse(cmd.Substring(7), inv)); return "even up " + cmd.Substring(7); }
if (cmd.StartsWith("givespool ")) { typeof(Protobot.Motion.Sandbox).GetField("GiveSpool", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).SetValue(null, cmd.EndsWith("1")); return "give spool " + cmd.Substring(10); }
if (cmd.StartsWith("maxgive ")) { typeof(Protobot.Motion.Sandbox).GetField("MaxGive", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).SetValue(null, float.Parse(cmd.Substring(8), inv)); return "max give " + cmd.Substring(8); }
if (cmd.StartsWith("skipat ")) { typeof(Protobot.Motion.Sandbox).GetField("SkipAt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).SetValue(null, float.Parse(cmd.Substring(7), inv)); return "skip at " + cmd.Substring(7); }
if (cmd == "debug") return Protobot.Motion.Sandbox.StringDebug();
if (cmd == "sim") { Protobot.Motion.Sandbox.Begin(); return "sim " + Protobot.Motion.Sandbox.Running; }
if (cmd == "stop") { Protobot.Motion.Sandbox.Stop(); return "stopped"; }
if (cmd.StartsWith("drive")) {
  var power = float.Parse(cmd.Substring(6), inv);
  foreach (var side in new[] { "R ", "L " }) {
    var m = P[side + "motor"];
    var rev = Protobot.Motion.Dynamics.Token(m, "rev") == "1";
    m.GetComponent<Protobot.Motion.MotorDrive>().Manual = rev ? -power : power;
  }
  return "manual " + power;
}
if (cmd.StartsWith("save ")) {
  var path = cmd.Substring(5).Trim();
  var data = Protobot.Builds.SceneBuild.ToBuildData(); data.name = System.IO.Path.GetFileNameWithoutExtension(path);
  Protobot.Builds.BuildFile.Write(path, data);
  return "saved " + data.parts.Length + " parts";
}
if (cmd.StartsWith("step")) {
  var n = int.Parse(cmd.Substring(5));
  for (var i = 0; i < n; i += 10) pt.GetMethod("Frames").Invoke(null, new object[] { 10 });
  cmd = "read";
}
var sb = new System.Text.StringBuilder();
if (cmd == "info" || !Protobot.Motion.Sandbox.Running) {
  foreach (var l in Protobot.Motion.StringLine.All) if (l != null) sb.AppendLine("line " + l.kind.key + " nodes " + l.nodes.Count + ": " + l.Describe().Replace('\n', ' ') + " | " + string.Join(" ", l.nodes.Select(n => (n.post.part != null ? n.post.part.name : "-") + "@" + n.post.Center.ToString("F2") + " r" + n.post.radius.ToString("F3", inv))));
  foreach (var b in Protobot.Motion.ElasticBand.All) if (b != null) sb.AppendLine("band " + b.Describe().Replace('\n', ' '));
  foreach (var r in Protobot.Motion.GearTrain.Rotors()) sb.AppendLine("rotor " + r.shaft.name + " parts " + string.Join(",", r.parts.Select(x => x.name)) + " motors " + r.motors.Count + " bearings " + r.bearings.Count);
  return sb.ToString();
}
foreach (var key in new[] { "R stage 2", "L stage 2", "carriage" }) {
  var part = P[key];
  var now = Protobot.Motion.Sandbox.SimPoint(part, part.transform.position);
  sb.AppendLine(key + " up " + (now.y - part.transform.position.y).ToString("F2", inv) + " side " + (now.x - part.transform.position.x).ToString("F3", inv) + " z " + (now.z - part.transform.position.z).ToString("F3", inv));
}
foreach (var s in Protobot.Motion.Sandbox.Strings()) sb.AppendLine(s.line.kind.key + " slack " + s.slack.ToString("F3", inv) + " pull " + s.tension.ToString("F1", inv) + " peak " + s.peak.ToString("F1", inv) + " moving " + s.moving + (s.broken ? " BROKEN" : "") + " wound " + Protobot.Motion.Sandbox.Wound(s.line).ToString("F2", inv));
foreach (var side in new[] { "R ", "L " }) { var md = P[side + "motor"].GetComponent<Protobot.Motion.MotorDrive>(); sb.AppendLine(side + "motor level " + md.Level.ToString("F2", inv) + " command " + md.Command.ToString("F2", inv)); }
return sb.ToString();
} catch (System.Exception e) { return "ERROR " + (e.InnerException ?? e).ToString(); }
