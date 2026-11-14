var o = new Protobot.Online.MatchSessions.HostOptions { name = "Agent test", mode = 0, maxPlayers = 4 };
var t = Protobot.Online.MatchSessions.Host(o);
System.AppDomain.CurrentDomain.SetData("hostTask", t);
return "started; cloud id " + UnityEngine.Application.cloudProjectId;
