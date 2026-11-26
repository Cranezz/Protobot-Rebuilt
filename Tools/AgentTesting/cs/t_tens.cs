// (Play) chain tensioner tests. Temp/tens.txt: build | finish | info | swing DEG | save PATH | sim | simstat | stop | run MOTOR SECONDS
var cmd = System.IO.File.Exists("Temp/tens.txt") ? System.IO.File.ReadAllText("Temp/tens.txt").Trim() : "info";
var R = Protobot.EditorTools.MotionTestRig.TensionerParts;
var inv = System.Globalization.CultureInfo.InvariantCulture;
System.Func<string> Info = () => {
    var sb = new System.Text.StringBuilder();
    foreach (var c in Protobot.Motion.ChainLoop.All) {
        if (c == null) continue;
        var t = Protobot.Motion.ChainTensioner.Of(c);
        sb.AppendLine("chain at x " + c.transform.position.x.ToString("F2", inv) + ": links " + c.Path.links + " fixed " + c.fixedLinks + " length " + c.Path.length.ToString("F4", inv) + " tight " + c.Path.tight.ToString("F4", inv) + " slack " + (c.Path.length - c.Path.tight).ToString("F4", inv) + " valid " + c.Path.Valid + (c.Path.Valid ? "" : " (" + c.Path.problem + ")")
            + " | tensioner " + (t == null ? "none" : "wraps " + string.Join(",", t.wraps) + " pull " + t.pull + " band " + (t.band != null) + " carried " + t.carried.Count + " arm " + t.armPart.name + " frame " + t.framePart.name));
        sb.AppendLine("  wraps: " + string.Join(" / ", c.wraps.Select(w => w.center.ToString("F3") + " r" + w.radius.ToString("F3", inv) + " way" + w.way + " t" + w.teeth)));
    }
    foreach (var k in new[] { "1 arm", "2 arm", "swing arm" }) if (R.ContainsKey(k) && R[k] != null) sb.AppendLine(k + " yaw " + R[k].transform.eulerAngles.y.ToString("F3", inv));
    foreach (var b in Protobot.Motion.ElasticBand.All) if (b != null) sb.AppendLine("band " + b.Describe());
    return sb.ToString();
};
if (cmd == "build") {
    var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
    if (Protobot.Motion.Sandbox.Running) Protobot.Motion.Sandbox.Stop();
    Protobot.PartsManager.DestroyLoadedObjects();
    var parts = Protobot.EditorTools.MotionTestRig.TensionerRig(Vector3.zero);
    return "parts " + parts.Count;
}
if (cmd == "finish") return Protobot.EditorTools.MotionTestRig.FinishTensioners() + Info();
if (cmd == "info") return Info();
if (cmd == "twochain") {
    // chain 1 round its drive and driven sprockets only (no idler), plain: the chain alone holds the swing arm
    var c = Protobot.Motion.ChainLoop.All.First(x => x != null && x.transform.position.x < 0f);
    var world = new System.Collections.Generic.List<(Vector3, Vector3, int, int)>();
    for (var i = 0; i < 2; i++) world.Add((c.transform.TransformPoint(c.wraps[i].center), c.Normal, c.wraps[i].teeth, 1));
    c.Set(c.family, world, c.style); c.Rebuild(); c.Connect();
    return "two-sprocket chain: links " + c.Path.links + " valid " + c.Path.Valid + " slack " + (c.Path.length - c.Path.tight).ToString("F3", inv);
}
if (cmd == "nofix") { foreach (var c in Protobot.Motion.ChainLoop.All) if (c != null) { c.fixedLinks = 0; c.Rebuild(); } return "plain chains" + Info(); }
if (cmd.StartsWith("swing ")) {
    // turns the swing arm's rotor (shaft, collar, channel, its screw, driven shaft and sprocket) about the swing shaft
    var deg = float.Parse(cmd.Substring(6), inv);
    var motor = R["swing motor"];
    var rotors = Protobot.Motion.GearTrain.Rotors();
    var rotor = rotors.FirstOrDefault(x => x.motors.Any(m => m.gameObject == motor));
    if (rotor == null) return "no swing rotor";
    var objs = new System.Collections.Generic.HashSet<GameObject>(rotor.parts); objs.Add(rotor.shaft);
    foreach (var other in rotors) if (other != rotor && other.bearings.Count > 0 && other.bearings.All(bb => rotor.parts.Contains(bb.part))) foreach (var pp in other.parts) objs.Add(pp);
    var moved = new System.Collections.Generic.List<string>();
    foreach (var o in objs) { o.transform.RotateAround(rotor.start, rotor.axis, deg); moved.Add(o.name); }
    // what rides on the channel (the driven shaft and sprocket)
    foreach (var k in new[] { "1 driven" }) if (!objs.Contains(R[k])) { moved.Add("+" + k); }
    return "rotated " + string.Join(", ", moved);
}
if (cmd.StartsWith("save ")) {
    var path = cmd.Substring(5).Trim();
    var data = Protobot.Builds.SceneBuild.ToBuildData(); data.name = System.IO.Path.GetFileNameWithoutExtension(path);
    Protobot.Builds.BuildFile.Write(path, data);
    return "saved " + data.parts.Length + " parts";
}
if (cmd == "sim") { if (!Protobot.Motion.Sandbox.Running) Protobot.Motion.Sandbox.Begin(); return "sim " + Protobot.Motion.Sandbox.Running; }
if (cmd == "stop") { Protobot.Motion.Sandbox.Stop(); return "stopped"; }
if (cmd == "simstat") {
    var sb = new System.Text.StringBuilder();
    foreach (var t in Protobot.Motion.Sandbox.Tensions()) sb.AppendLine("tension x " + t.chain.transform.position.x.ToString("F1", inv) + " slack " + t.slack.ToString("F4", inv) + " turned " + t.turned.ToString("F3", inv));
    foreach (var t in Protobot.Motion.Sandbox.ChainStops()) sb.AppendLine("stop x " + t.chain.transform.position.x.ToString("F1", inv) + " slack " + t.slack.ToString("F4", inv) + " moving " + t.moving);
    for (var k = 0; k < 2; k++) sb.AppendLine("detail " + k + ": " + Protobot.Motion.Sandbox.ChainStopDetail(k));
    var arm = R.ContainsKey("swing arm") ? R["swing arm"] : null; var rb = arm != null ? Protobot.Motion.Sandbox.BodyOf(arm) : null;
    if (rb != null) sb.AppendLine("swing arm body yaw " + rb.rotation.eulerAngles.ToString("F1"));
    sb.AppendLine("t " + Time.time.ToString("F2", inv));
    return sb.ToString();
}
if (cmd.StartsWith("run ")) {
    var f = cmd.Split(' ');
    var m = Protobot.Motion.MotorDrive.Live().FirstOrDefault(x => Protobot.Motion.Controls.GivenName(x.gameObject) == f[1].Replace('_', ' '));
    if (m == null) return "no motor " + f[1];
    m.Manual = float.Parse(f[2], inv);
    return "manual " + m.Manual;
}
if (cmd.StartsWith("speed ")) {
    var f = cmd.Split(' ');
    var m = UnityEngine.Object.FindObjectsByType<Protobot.Motion.MotorDrive>(FindObjectsSortMode.None).FirstOrDefault(x => Protobot.Motion.Controls.GivenName(x.gameObject) == f[1].Replace('_', ' '));
    if (m == null) return "no motor " + f[1];
    Protobot.Motion.Controls.SetSpeed(m.gameObject, float.Parse(f[2], inv));
    return "speed " + Protobot.Motion.Controls.Speed(m.gameObject);
}
return "?";
