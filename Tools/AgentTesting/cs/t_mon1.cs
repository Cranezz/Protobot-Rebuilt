// Interpreter self-test + octal escapes (Python and C++) + a thread snapshot with locals.
var sb = new System.Text.StringBuilder();
var self = Protobot.Scripting.SelfTest.Run();
sb.AppendLine(self.Split('\n')[0]);
var vm = new Protobot.Scripting.Interpreter();
var outText = new System.Text.StringBuilder();
vm.Output = s => outText.Append(s);
vm.Load("x = '\\033[31m'\nprint(len(x), ord(x[0]))\ndef f(a, b):\n    c = a + b\n    d = [1, 2.5, 'hi']\n    while True:\n        pass\nf(3, 4)\n");
vm.Step();
sb.AppendLine("py octal: " + outText.ToString().Trim());
var fiber = vm.fibers[0];
foreach (var fr in fiber.Snapshot())
    sb.AppendLine("frame " + (fr.function ?? "<top>") + " line " + fr.line + " locals " + string.Join(", ", fr.locals.Select(kv => kv.Key + "=" + Protobot.RobotCode.CodeMonitor.Short(kv.Value, 0))));
var cpp = Protobot.Scripting.CppTranslator.Translate("#include <cstdio>\nint main() { printf(\"\\033[2J%d\\n\", 5); return 0; }\n", out var warns);
var vm2 = new Protobot.Scripting.Interpreter();
var out2 = new System.Text.StringBuilder();
vm2.Output = s => out2.Append(s);
Protobot.Scripting.CppRuntime.Install(vm2);
vm2.Load(Protobot.Scripting.Compiler.CompileModule(cpp));
for (var i = 0; i < 5; i++) vm2.Step();
sb.AppendLine("cpp octal: " + string.Join(" ", out2.ToString().Select(c => ((int)c).ToString())) + (vm2.Failed != null ? " FAILED " + vm2.Failed.Message : ""));
return sb.ToString();
