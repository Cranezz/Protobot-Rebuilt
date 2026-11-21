// Writes the Odometry example filled for this robot to Tools/AgentTesting/odom/py_example.py, its prints as marks.
var e = Protobot.RobotCode.Examples.All.First(x => x.name.Contains("dometry"));
var code = Protobot.RobotCode.Examples.Fill(e.code).Replace("print(\"pose:\"", "print(\"mark pose:\"").Replace("print(\"back at\"", "print(\"mark back at\"");
System.IO.File.WriteAllText("Tools/AgentTesting/odom/py_example.py", code);
return code.Length + " chars";
