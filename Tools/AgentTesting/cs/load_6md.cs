var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
Protobot.PartsManager.DestroyLoadedObjects();
var data = Protobot.Builds.BuildFile.Read(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/builds/6 Motor Drive (3.25 omni, 360 rpm).pbb");
Protobot.Builds.SceneBuild.GenerateBuild(data);
return "parts " + data.parts.Length;
