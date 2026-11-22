// (Play) Trailer: motion shot helpers. Temp/motion.txt: "exitgame", "list" (motors, names, positions), "run P" (every
// motor's manual power P), "stop", "orbitat NAME SECONDS X Y Z RADIUS HEIGHT START SPEED HIDE" (orbit a point).
var cmd = System.IO.File.ReadAllText("Temp/motion.txt").Trim().Split(' ');
System.Func<int, float> F = i => float.Parse(cmd[i], System.Globalization.CultureInfo.InvariantCulture);
if (cmd[0] == "exitgame") { if (Protobot.Game.SkillsGame.Current != null) Protobot.Game.SkillsGame.Current.Exit(); return "exited"; }
if (cmd[0] == "list") {
    var sb = new System.Text.StringBuilder();
    foreach (var m in Protobot.Motion.MotorDrive.Live()) sb.Append(m.name + " at " + m.transform.position.ToString("F0") + " | ");
    return sb.ToString();
}
if (cmd[0] == "run") { foreach (var m in Protobot.Motion.MotorDrive.Live()) m.Manual = F(1); return "running " + Protobot.Motion.MotorDrive.Live().Count; }
if (cmd[0] == "stop") { foreach (var m in Protobot.Motion.MotorDrive.Live()) m.Manual = 0f; return "stopped"; }
if (cmd[0] == "orbitat") {
    Protobot.Builds.FrameRecorder.Record("C:/Dev/ProtobotRebuiltV2/Video/shots/" + cmd[1], F(2));
    if (cmd[10] == "1") Protobot.Builds.FrameRecorder.HideInterface(true);
    Protobot.Builds.FrameRecorder.Orbit(new UnityEngine.Vector3(F(3), F(4), F(5)), null, F(6), F(7), F(8), F(9));
    return "recording " + cmd[1];
}
return "?";
