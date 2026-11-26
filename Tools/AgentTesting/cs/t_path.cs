// (Play) path planner test. Temp/path.txt:
//   load            open the odometry sample build (closes the home menu)
//   plan FORMAT     (a game open) a curve from the robot's start: 24" ahead, curving 60° right, then 12" more; the
//                   program = the odometry example up to calibrate(), the planner's code inserted after it
//                   (FORMAT 0 follow, 1 one move per point); shows the program's end
//   run             starts the run (Programming Skills: the program drives)
//   trail           the robot's path so far vs the plan: points, how far its end is from the plan's, the farthest the
//                   plan is from the path, and the odometry's last pose
//   shot            the planner open, screenshot Temp/planner.png
var cmd = System.IO.File.ReadAllText("Temp/path.txt").Trim().Split(' ');
var inv = System.Globalization.CultureInfo.InvariantCulture;
var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
if (cmd[0] == "load") {
    Protobot.PartsManager.DestroyLoadedObjects();
    var data = Protobot.Builds.BuildFile.Read(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Sample Builds/Override Demo Robot.pbb");
    Protobot.Builds.SceneBuild.GenerateBuild(data);
    return "parts " + data.parts.Length;
}
var g = Protobot.Game.SkillsGame.Current;
var type = typeof(Protobot.RobotCode.PathPlanner);
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
System.Func<float, Vector2> Dir = h => new Vector2(Mathf.Sin(h * Mathf.Deg2Rad), Mathf.Cos(h * Mathf.Deg2Rad));
if (cmd[0] == "plan") {
    if (g == null) return "no game";
    var p0 = new Vector2(g.x, g.y);
    var p1 = p0 + Dir(g.heading) * 24f;
    var p2 = p1 + Dir(g.heading + 30f) * 14f;
    var p3 = p2 + Dir(g.heading + 60f) * 14f;
    System.Func<Vector2, int, string> P = (v, s) => "{\"x\":" + v.x.ToString(inv) + ",\"y\":" + v.y.ToString(inv) + ",\"speed\":" + s + "}";
    var json = "{\"name\":\"test1\",\"points\":[" + P(p0, 80) + "," + P(p1, 80) + "," + P(p2, 60) + "," + P(p3, 60) + "],\"heading\":" + g.heading.ToString(inv)
               + ",\"lookahead\":12,\"timeout\":6000,\"backwards\":false,\"format\":" + cmd[1] + "}";
    PlayerPrefs.SetString("V2.PathPlanner.Last", json);
    var cpp = cmd.Length > 2 && cmd[2] == "cpp";
    var example = Protobot.RobotCode.Examples.All.First(e => e.name.StartsWith(cpp ? "C++: PROS + LemLib" : "Odometry and motions")).code;
    var filled = Protobot.RobotCode.Examples.Fill(example);
    var cut = cpp ? filled : filled.Substring(0, filled.IndexOf("chassis.calibrate()") + "chassis.calibrate()".Length) + "\n";
    if (!cut.Contains("imu = ")) cut = cut.Replace(", imu)", ", None)");   // no Inertial on this robot: odometry from the drive alone
    Protobot.RobotCode.CodeStore.Set(cut);
    Protobot.RobotCode.PathPlanner.Open();
    var planner = UnityEngine.Object.FindAnyObjectByType<Protobot.RobotCode.PathPlanner>();
    type.GetMethod("Insert", flags).Invoke(planner, null);
    var src = Protobot.RobotCode.CodeStore.Source;
    if (cpp) {
        var a = src.IndexOf("ASSET(");
        var au = src.IndexOf("void autonomous");
        return "ASSET at " + a + ": " + (a >= 0 ? src.Substring(System.Math.Max(0, a - 120), 160) : "") + "\n---\n" + (au >= 0 ? src.Substring(au, System.Math.Min(600, src.Length - au)) : "no autonomous")
               + "\n---files: " + string.Join(", ", Protobot.RobotCode.CodeProject.Split(src).Select(x => x.path));
    }
    return "program end:\n" + src.Substring(System.Math.Max(0, src.Length - 900));
}
if (cmd[0] == "run") { g.StartRun(); return "state " + g.state + " mode " + g.mode; }
if (cmd[0] == "trail") {
    var real = Protobot.RobotCode.PathTrail.Real; var odom = Protobot.RobotCode.PathTrail.Odom;
    var pts = new System.Collections.Generic.List<Vector2>();
    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(PlayerPrefs.GetString("V2.PathPlanner.Last"), "[{]\"x\":([-0-9.eE]+),\"y\":([-0-9.eE]+)"))
        pts.Add(new Vector2(float.Parse(m.Groups[1].Value, inv), float.Parse(m.Groups[2].Value, inv)));
    var sb = new System.Text.StringBuilder("state " + (g != null ? g.state.ToString() : "-") + " time " + (g != null ? g.timeLeft.ToString("0.0") : "") + " real " + real.Count + " odom " + odom.Count);
    if (real.Count > 0) sb.Append(" robot at " + real[real.Count - 1].ToString("F1"));
    if (odom.Count > 0) sb.Append(" odom " + odom[odom.Count - 1].ToString("F1"));
    if (pts.Count > 0 && real.Count > 0) {
        var end = pts[pts.Count - 1];
        sb.Append(" end miss " + Vector2.Distance(end, real[real.Count - 1]).ToString("F2") + "\"");
        var worst = 0f;
        for (var i = 0; i + 1 < pts.Count; i++)
            for (var t = 0f; t <= 1f; t += 0.1f) {
                var q = Vector2.Lerp(pts[i], pts[i + 1], t);
                worst = Mathf.Max(worst, real.Min(r => Vector2.Distance(r, q)));
            }
        sb.Append(" farthest " + worst.ToString("F2") + "\"");
    }
    return sb.ToString();
}
if (cmd[0] == "shot") {
    if (!Protobot.RobotCode.PathPlanner.IsOpen) Protobot.RobotCode.PathPlanner.Open();
    UnityEngine.ScreenCapture.CaptureScreenshot(System.IO.Path.GetFullPath("Temp/planner.png"));
    return "shot";
}
return "?";
