// What threads does the running program have (all fibers, done or not)?
var p = Protobot.RobotCode.RobotProgram.Current;
if (p == null) return "no program";
var sb = new System.Text.StringBuilder();
foreach (var f in p.vm.fibers) sb.AppendLine(f.name + " done=" + f.Done + " stopped=" + f.stopped + " sync=" + f.Sync + " line=" + f.Line);
sb.AppendLine("ticks=" + (p.vm.main.globals.TryGetValue("ticks", out var t) ? t : "?"));
var src = Protobot.Scripting.CppTranslator.Translate(Protobot.RobotCode.CodeStore.Source, out var w);
sb.AppendLine("warnings: " + string.Join(" | ", w));
return sb.ToString();
