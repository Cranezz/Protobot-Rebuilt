var s = Protobot.Online.MatchSessions.Current;
s.CurrentPlayer.SetProperty("team", new Unity.Services.Multiplayer.PlayerProperty("red"));
s.CurrentPlayer.SetProperty("robot", new Unity.Services.Multiplayer.PlayerProperty("6 Motor Drive with intake"));
Protobot.Online.MatchSessions.Set("ready", "1");
return "red + ready";
