var g = Protobot.Game.SkillsGame.Current;
g.SetTurn(20f);
g.FrameCamera();
return "ok " + g.Turn;
