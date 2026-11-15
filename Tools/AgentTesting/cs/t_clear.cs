var g = Protobot.Game.SkillsGame.Current;
var t = typeof(Protobot.Game.SkillsGame);
var m = t.GetMethod("ClearOfPieces", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
if (m == null) return "no ClearOfPieces (old code)";
var src = System.IO.File.ReadAllText("C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Assets/ProtobotRebuilt/Scripts/Game/SkillsGame.cs").Contains("turned sideways");
g.DefaultStart();
return "source new " + src + "; after DefaultStart: " + g.x.ToString("F1") + "," + g.y.ToString("F1") + " heading " + g.heading + " legal " + g.Legal;
