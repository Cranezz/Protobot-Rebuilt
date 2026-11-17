// How do pros::Task declarations translate? (dump of the Python AST as source-ish text is not available: run a tiny program)
var srcs = new[] {
    "#include \"main.h\"\nint ticks = 0;\nvoid count_task() {\n  while (1) { ticks++; pros::delay(10); }\n}\nvoid opcontrol() {\n  pros::Task counter(count_task);\n  pros::delay(100);\n  printf(\"A %d\\n\", ticks);\n}\n",
    "#include \"main.h\"\nint ticks = 0;\nvoid count_task() {\n  while (1) { ticks++; pros::delay(10); }\n}\nvoid opcontrol() {\n  pros::Task counter{count_task};\n  pros::delay(100);\n  printf(\"B %d\\n\", ticks);\n}\n",
    "#include \"main.h\"\nint ticks = 0;\nvoid count_task() {\n  while (1) { ticks++; pros::delay(10); }\n}\nvoid opcontrol() {\n  pros::Task counter(count_task, \"counter\");\n  pros::delay(100);\n  printf(\"C %d\\n\", ticks);\n}\n",
};
var log = new System.Text.StringBuilder();
System.Action<string> h = s => log.Append(s);
Protobot.RobotCode.RobotProgram.Printed += h;
foreach (var s in srcs) {
    string err; int line;
    var ok = Protobot.RobotCode.RobotProgram.Start(s, Protobot.RobotCode.RobotProgram.Period.Driver, 0, out err, out line);
    if (!ok) { log.Append("start failed " + err + "\n"); continue; }
    var p = Protobot.RobotCode.RobotProgram.Current;
    for (var i = 0; i < 60; i++) Protobot.RobotCode.RobotProgram.Step(1f / 240f);
    log.Append("fibers: " + string.Join(",", p.vm.fibers.Select(f => f.name + (f.Done ? "(done)" : ""))) + "\n");
}
Protobot.RobotCode.RobotProgram.Printed -= h;
Protobot.RobotCode.RobotProgram.Stop(null);
return log.ToString();
