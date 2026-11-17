// Offline: the online test program's driver part (1 s spin at 3 V) and its autonomous, measured on a C-channel.
var period = UnityEngine.PlayerPrefs.GetString("t_spin_period", "Driver") == "Driver" ? Protobot.RobotCode.RobotProgram.Period.Driver : Protobot.RobotCode.RobotProgram.Period.Autonomous;
Protobot.Motion.Sandbox.Stop(); Protobot.Motion.Sandbox.Begin();
var ch = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.InstanceID).First(s => s.id.StartsWith("CC")).gameObject;
System.AppDomain.CurrentDomain.SetData("spinPart", ch);
string err; int line;
var ok = Protobot.RobotCode.RobotProgram.Start(System.IO.File.ReadAllText(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/online_test_program.py"), period, 10, out err, out line);
return period + " started " + ok + " " + err;
