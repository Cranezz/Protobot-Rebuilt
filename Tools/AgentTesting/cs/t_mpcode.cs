var s = Protobot.Online.MatchSessions.Current; return s == null ? "none" : "code " + s.Code + " players " + s.PlayerCount;
