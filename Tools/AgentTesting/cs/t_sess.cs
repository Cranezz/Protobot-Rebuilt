var s = Protobot.Online.MatchSessions.Current; return s == null ? "no session" : "session " + s.Name + " players " + s.PlayerCount;
