// The last replay: size and contents.
Protobot.Motion.Sandbox.TestSticks = null;
var g = Protobot.Game.SkillsGame.Current;
var r = Protobot.Game.Replays.Last;
if (r == null) return "no replay (state " + (g != null ? g.state.ToString() : "no game") + ")";
var robot = 0; var pieces = 0; var proxies = 0; var mapped = 0;
foreach (var o in r.objects) { if (o.robot) robot++; else pieces++; proxies += o.proxies.Length; foreach (var p in o.proxies) if (p.part >= 0) mapped++; }
var bytes = Protobot.Game.Replays.ToBytes(r).Length;
var decoded = Protobot.Game.Replays.Decode(r);
return "score " + r.score + ", " + r.frames + " frames, robot bodies " + robot + ", pieces " + pieces + ", proxies " + proxies + " (" + mapped + " from parts), file " + bytes / 1024 + " KB, decoded " + decoded.Length;
