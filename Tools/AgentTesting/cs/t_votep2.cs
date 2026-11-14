var s = Protobot.Online.MatchSessions.Current;
string target = null; var sb = new System.Text.StringBuilder();
foreach (var p in s.Players) { sb.Append(Protobot.Online.MatchSessions.Get(p, "name") + "(" + p.Id.Substring(0, 6) + ") "); if (target == null && p.Id != s.Host) target = p.Id; }
if (target != null) Protobot.Online.MatchSessions.VoteKick(target);
System.IO.File.WriteAllText("C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/target.txt", target ?? "");
return "players " + sb + "| voted for " + (target ?? "-").Substring(0, 6) + " votes " + Protobot.Online.MatchSessions.KickVotes(target) + "/" + Protobot.Online.MatchSessions.KickVotesNeeded();
