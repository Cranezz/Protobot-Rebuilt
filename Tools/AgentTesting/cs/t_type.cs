var a = System.Type.GetType("Protobot.Online.PlayerReports, Assembly-CSharp");
var b = typeof(Protobot.Online.OnlineMatch).GetMethod("PlayersDialog");
var c = typeof(Protobot.Online.MatchSessions).GetMethod("VoteKick");
return (a != null) + " " + (b != null) + " " + (c != null);
