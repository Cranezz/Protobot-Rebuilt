var code = System.IO.File.ReadAllText("C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/code.txt").Trim();
var t = Protobot.Online.MatchSessions.Join(code, false, null);
System.AppDomain.CurrentDomain.SetData("joinTask", t);
return "joining " + code;
