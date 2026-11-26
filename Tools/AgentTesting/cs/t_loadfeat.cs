var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
if (Protobot.Motion.Sandbox.Running) Protobot.Motion.Sandbox.Stop();
Protobot.PartsManager.DestroyLoadedObjects();
var data = Protobot.Builds.BuildFile.Read(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Sample Builds/V2 Features.pbb");
Protobot.Builds.SceneBuild.GenerateBuild(data);
return "parts " + data.parts.Length;
