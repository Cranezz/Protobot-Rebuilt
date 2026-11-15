var data = Protobot.Builds.SceneBuild.ToBuildData(); data.name = "Override Demo Robot";
var path = @"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Sample Builds/Override Demo Robot.pbb";
Protobot.Builds.BuildFile.Write(path, data);
return "saved " + data.parts.Length + " parts";
