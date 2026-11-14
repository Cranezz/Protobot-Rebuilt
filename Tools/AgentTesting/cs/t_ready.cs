Protobot.Online.OnlineMatch.TestAllowIllegal = true;
var g = Protobot.Game.SkillsGame.Current;
if (g == null) return "no game";
g.onlineReady?.Invoke();
return "ready pressed; locked " + g.onlineLocked + " legal " + g.Legal;
