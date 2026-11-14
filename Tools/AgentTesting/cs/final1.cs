Protobot.PartsManager.DestroyLoadedObjects();
var load = Protobot.Builds.BuildFile.Read(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/builds/6 Motor Drive with intake.pbb");
Protobot.Builds.SceneBuild.GenerateBuild(load);
return "loaded " + load.parts.Length;
