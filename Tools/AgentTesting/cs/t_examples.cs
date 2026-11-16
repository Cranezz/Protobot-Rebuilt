var sb = new System.Text.StringBuilder();
foreach (var e in Protobot.RobotCode.Examples.All) {
  var code = Protobot.RobotCode.Examples.Fill(e.code);
  try { Protobot.Scripting.Compiler.Compile(code); sb.AppendLine("OK   " + e.name + (code.Contains("{") && code.Contains("}") && (code.Contains("{track}") || code.Contains("{devices}")) ? "  (UNFILLED)" : "")); }
  catch (System.Exception ex) { sb.AppendLine("FAIL " + e.name + ": " + ex.Message); }
}
return sb.ToString();
