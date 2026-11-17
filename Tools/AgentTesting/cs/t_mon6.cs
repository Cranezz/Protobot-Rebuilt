// Which line does a thread report while it waits, in C++ and Python? (expected: the wait's own line)
var sb = new System.Text.StringBuilder();
var cpp = "#include \"main.h\"\nint n = 0;\nvoid opcontrol() {\n  printf(\"hi\\n\");\n  while (1) {\n    n++;\n    pros::delay(20);\n  }\n}\n";
var py = "from vex import *\nn = 0\nwhile True:\n    n += 1\n    wait(20, MSEC)\n";
var names = new[] { "cpp", "py" }; var srcs = new[] { cpp, py }; var wants = new[] { 7, 5 };
for (var k = 0; k < 2; k++) {
    var name = names[k]; var src = srcs[k]; var want = wants[k];
    string err; int line;
    var ok = Protobot.RobotCode.RobotProgram.Start(src, Protobot.RobotCode.RobotProgram.Period.Driver, 0, out err, out line);
    var p = Protobot.RobotCode.RobotProgram.Current;
    for (var i = 0; i < 30; i++) Protobot.RobotCode.RobotProgram.Step(1f / 240f);
    sb.AppendLine(name + " ok=" + ok + " " + err + " lines: " + string.Join(",", p.Threads.Select(f => f.name + "@" + f.Line)) + " (want " + want + ")");
}
Protobot.RobotCode.RobotProgram.Stop(null);
return sb.ToString();
