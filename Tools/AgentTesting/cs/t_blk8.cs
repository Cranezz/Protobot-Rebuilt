// (Play mode, the 6-motor drive loaded) each blocks example made on this robot: its Python compiles.
var config = Protobot.RobotCode.DeviceConfig.Generate().TrimEnd('\n', '\r');
var sb = new System.Text.StringBuilder();
for (var i = 0; i < Protobot.RobotCode.Blocks.BlockExamples.Names.Length; i++) {
    var p = Protobot.RobotCode.Blocks.BlockExamples.Make(i, config);
    var src = p.ToSource(out var code);
    string r;
    try { Protobot.Scripting.Compiler.Compile(src); r = "ok"; } catch (System.Exception e) { r = "ERROR " + e.Message; }
    sb.AppendLine(Protobot.RobotCode.Blocks.BlockExamples.Names[i] + ": " + r + (src.Contains("pass  #") ? " (has a missing-device pass)" : ""));
    if (i == 1) sb.AppendLine(string.Join("\n", code.python.Split('\n').Where(l => l.StartsWith("    ") || l.StartsWith("def ")).Take(12)));
}
return sb.ToString();
