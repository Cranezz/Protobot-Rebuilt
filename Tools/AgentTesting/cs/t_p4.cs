var g = Protobot.Game.SkillsGame.Current;
var od = new Protobot.Builds.ObjectData(); od.partId = "CCHL-1x2-25";
g.LoadToolParts(new[] { od }, "test jig");
g.setupTools.toolSelected = false;
g.FrameCamera();
return "tool at " + g.toolX + "," + g.toolY;
