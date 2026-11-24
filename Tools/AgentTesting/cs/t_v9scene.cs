// (Play) Tutorial 5's start: the Override demo robot.
var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
Protobot.PartsManager.DestroyLoadedObjects();
Protobot.Builds.DemoPlayer.Target.Clear();
var data = Protobot.Builds.BuildFile.Read("C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Sample Builds/Override Demo Robot.pbb");
Protobot.Builds.SceneBuild.GenerateBuild(data);
return "parts " + data.parts.Length;
