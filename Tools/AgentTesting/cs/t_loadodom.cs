// Opens Sample Builds/Odometry Robot (tracking wheels + IMU).pbb and writes its program (prints as marks) to
// Tools/AgentTesting/odom/py_loaded.py; says what it has (joint screws, bands, sensors).
var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
Protobot.RobotCode.RobotProgram.Stop("test"); Protobot.Motion.Sandbox.Stop();
Protobot.PartsManager.DestroyLoadedObjects();
var data = Protobot.Builds.BuildFile.Read(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Sample Builds/Odometry Robot (tracking wheels + IMU).pbb");
Protobot.Builds.SceneBuild.GenerateBuild(data);
var code = (data.code ?? "").Replace("print(\"pose:\"", "print(\"mark pose:\"").Replace("print(\"back at\"", "print(\"mark back at\"");
System.IO.File.WriteAllText("Tools/AgentTesting/odom/py_loaded.py", code);
var joints = data.parts.Count(p => p.states != null && p.states.Contains("joint:1"));
var bands = data.parts.Count(p => p.states != null && p.states.Contains("loop:"));
var named = data.parts.Count(p => p.states != null && p.states.Contains("name:"));
return "parts " + data.parts.Length + ", joint screws " + joints + ", bands " + bands + ", named " + named + ", code " + code.Length;
