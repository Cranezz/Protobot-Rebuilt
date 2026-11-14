var s = Protobot.Online.MatchSessions.Current;
var net = Unity.Netcode.NetworkManager.Singleton;
var sb = new System.Text.StringBuilder("players " + s.PlayerCount + " | net clients " + net.ConnectedClientsIds.Count);
foreach (var p in s.Players) sb.Append(" | " + Protobot.Online.MatchSessions.Get(p, "name") + " team=" + Protobot.Online.MatchSessions.Get(p, "team"));
return sb.ToString();
