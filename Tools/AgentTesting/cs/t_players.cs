var s = Protobot.Online.MatchSessions.Current;
var sb = new System.Text.StringBuilder("players " + s.PlayerCount + ": ");
foreach (var p in s.Players) sb.Append(p.Id.Substring(0, 6) + " kick=" + Protobot.Online.MatchSessions.Get(p, "kick").Substring(0, System.Math.Min(6, Protobot.Online.MatchSessions.Get(p, "kick").Length)) + " | ");
return sb.ToString();
