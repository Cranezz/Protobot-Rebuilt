// Runs project number PlayerPrefs "t_proj_index" of the list (t_projset.cs) on the loaded build: period from
// "t_hdr_period" (a = autonomous, d = driver), output to "t_code_marks"; the robot's start pose to "t_proj_start".
var list = System.IO.File.ReadAllLines(UnityEngine.PlayerPrefs.GetString("t_proj_list")).Where(l => l.Trim() != "").ToList();
var dir = list[UnityEngine.PlayerPrefs.GetInt("t_proj_index")];
var parts = new System.Collections.Generic.List<string>();
foreach (var inc in new[] { "include", "include (1)" }) {
    var d = System.IO.Path.Combine(dir, inc);
    if (!System.IO.Directory.Exists(d)) continue;
    foreach (var f in System.IO.Directory.GetFiles(d, "*.h*")) {
        var n = System.IO.Path.GetFileName(f).ToLower();
        if (n.StartsWith("vex") || n == "main.h" || n == "api.h") continue;
        parts.Add(f);
    }
}
foreach (var s in new[] { "src", "src (1)" }) {
    var d = System.IO.Path.Combine(dir, s);
    if (!System.IO.Directory.Exists(d)) continue;
    parts.AddRange(System.IO.Directory.GetFiles(d, "*.c*").Where(f => !f.Contains("(1)") || s.Contains("(1)")).OrderBy(f => System.IO.Path.GetFileName(f).StartsWith("robot-config") ? 0 : 1));
}
var code = string.Join("\n", parts.Select(f => System.IO.File.ReadAllText(f)));
Protobot.RobotCode.CodeStore.Set(code);
Protobot.Motion.Sandbox.Stop(); Protobot.Motion.Sandbox.Begin();
var rb = Protobot.Motion.Sandbox.Instance.Bodies().OrderByDescending(b => b.rb.mass).First().rb;
UnityEngine.PlayerPrefs.SetString("t_proj_start", rb.position.x + "," + rb.position.z + "," + rb.rotation.eulerAngles.y);
var log = new System.Text.StringBuilder();
UnityEngine.PlayerPrefs.SetString("t_code_marks", "");
Protobot.RobotCode.RobotProgram.Printed += s => { if (log.Length < 6000) log.Append(s); UnityEngine.PlayerPrefs.SetString("t_code_marks", log.ToString()); };
string err; int line;
var period = UnityEngine.PlayerPrefs.GetString("t_hdr_period") == "d" ? Protobot.RobotCode.RobotProgram.Period.Driver : Protobot.RobotCode.RobotProgram.Period.Autonomous;
var ok = Protobot.RobotCode.RobotProgram.Start(code, period, 15, out err, out line);
return System.IO.Path.GetFileName(dir) + ": started " + ok + " " + err + " line " + line;
