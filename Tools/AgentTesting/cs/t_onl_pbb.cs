// Online robot code test: the 6-motor drive with online_test_program.py (autonomous 0.8 s at 4 V, driver 1 s spin), for the joining copy (-open Temp/6md_code.pbb).
// Copies the 6-motor drive with the online test program in it (for the joining copy).
var dir = @"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/";
var data = Protobot.Builds.BuildFile.Read(dir + "builds/6 Motor Drive (3.25 omni, 360 rpm).pbb");
data.code = System.IO.File.ReadAllText(dir + "online_test_program.py");
Protobot.Builds.BuildFile.Write(dir + "Temp/6md_code.pbb", data);
return "written, code " + data.code.Length + " chars";
