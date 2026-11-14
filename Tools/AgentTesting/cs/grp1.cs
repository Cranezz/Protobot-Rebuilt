Protobot.PartsManager.DestroyLoadedObjects();
var load = Protobot.Builds.BuildFile.Read(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/builds/V2 Features.pbb");
Protobot.Builds.SceneBuild.GenerateBuild(load);
return "loaded " + load.parts.Length;
