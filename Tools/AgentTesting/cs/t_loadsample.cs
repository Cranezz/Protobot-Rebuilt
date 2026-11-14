var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
Protobot.PartsManager.DestroyLoadedObjects();
var name = System.IO.File.ReadAllText("Temp/sample.txt").Trim();
var data = Protobot.Builds.BuildFile.Read(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Sample Builds/" + name);
Protobot.Builds.SceneBuild.GenerateBuild(data);
return name + ": parts " + data.parts.Length;
