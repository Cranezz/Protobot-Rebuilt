// (Play, host) Lists the lobby's players and tries Start match. Result: each player's team/robot/ready and HostStart's answer.
var s = Protobot.Online.MatchSessions.Current;
if (s == null) return "not in a match";
var sb = new System.Text.StringBuilder();
foreach (var p in s.Players) sb.Append(Protobot.Online.MatchSessions.Get(p, Protobot.Online.MatchSessions.NameKey) + ": team " + Protobot.Online.MatchSessions.Get(p, Protobot.Online.MatchSessions.TeamKey) + ", robot '" + Protobot.Online.MatchSessions.Get(p, Protobot.Online.MatchSessions.RobotKey) + "', ready " + Protobot.Online.MatchSessions.Get(p, Protobot.Online.MatchSessions.ReadyKey) + " | ");
var problem = Protobot.Online.OnlineMatch.HostStart();
return sb + "start: " + (problem ?? "started") + " | code " + s.Code;
