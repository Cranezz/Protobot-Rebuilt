// Translates every project folder listed in the scratchpad's projects.txt (VEXcode/PROS: include/*.h user headers,
// src/*.cpp with robot-config first) and compiles it; prints errors and warnings per project.
var list = System.IO.File.ReadAllLines(UnityEngine.PlayerPrefs.GetString("t_proj_list"));
var sb = new System.Text.StringBuilder();
foreach (var dir in list) {
    if (dir.Trim() == "") continue;
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
        var files = System.IO.Directory.GetFiles(d, "*.c*").OrderBy(f => System.IO.Path.GetFileName(f).StartsWith("robot-config") ? 0 : 1).ToList();
        parts.AddRange(files);
    }
    var src = string.Join("\n", parts.Select(f => System.IO.File.ReadAllText(f)));
    sb.Append("== " + System.IO.Path.GetFileName(dir) + " (" + parts.Count + " files, " + src.Split('\n').Length + " lines)\n");
    try {
        System.Collections.Generic.List<string> warnings;
        var body = Protobot.Scripting.CppTranslator.Translate(src, out warnings);
        Protobot.Scripting.Compiler.CompileModule(body);
        foreach (var w in warnings.Distinct()) sb.Append("   warn: " + w + "\n");
        sb.Append("   ok\n");
    }
    catch (System.Exception e) { sb.Append("   ERROR: " + e.Message + (e is Protobot.Scripting.SyntaxErr ? " (line " + ((Protobot.Scripting.SyntaxErr)e).line + ")" : "") + "\n"); }
}
return sb.ToString();
