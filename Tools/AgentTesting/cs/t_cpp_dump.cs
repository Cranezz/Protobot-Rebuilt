var src = "#include \"vex.h\"\nusing namespace vex;\n\nbrain Brain;\nint main() { return 0; }\n";
System.Collections.Generic.List<string> w;
var body = Protobot.Scripting.CppTranslator.Translate(src, out w);
var sb = new System.Text.StringBuilder();
foreach (var s in body) {
  sb.Append(s.GetType().Name + " line " + s.line);
  var im = s as Protobot.Scripting.ImportStmt; if (im != null) sb.Append(" module=" + im.module + " star=" + im.star);
  var a = s as Protobot.Scripting.AssignStmt; if (a != null) sb.Append(" value=" + a.value.GetType().Name);
  sb.AppendLine();
}
return sb.ToString();
