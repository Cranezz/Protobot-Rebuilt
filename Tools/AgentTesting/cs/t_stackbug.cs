var results = new System.Text.StringBuilder();
var programs = new string[] {
  "from vex import *\nbrain = Brain()\nprint('mark A', brain.timer.time(SECONDS))\nprint('next')\n",
  "from vex import *\nbrain = Brain()\nx = brain.timer.time(SECONDS)\nprint('mark A', x)\nprint('next')\n",
  "from vex import *\nprint('mark A', 1.5)\nprint('next')\n",
  "print('mark A', 1.5)\nprint('next')\n",
};
foreach (var src in programs) {
  string err; int line;
  var ok = Protobot.RobotCode.RobotProgram.Start(src, Protobot.RobotCode.RobotProgram.Period.Program, 0, out err, out line);
  for (var i = 0; i < 5; i++) Protobot.RobotCode.RobotProgram.Step(0.01f);
  var p = Protobot.RobotCode.RobotProgram.Current;
  results.AppendLine(src.Replace("\n", " / ") + " => " + (p == null ? "null" : p.Status));
  Protobot.RobotCode.RobotProgram.Stop(null);
}
return results.ToString();
