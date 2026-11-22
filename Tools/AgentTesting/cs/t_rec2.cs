var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
Protobot.PartsManager.DestroyLoadedObjects();
var data = Protobot.Builds.BuildFile.Read(@"C:/Users/kille/AppData/LocalLow/Protobot/ProtobotRebuiltV2/Recovery/recovery.pbb");
Protobot.Builds.SceneBuild.GenerateBuild(data);
var sb = new System.Text.StringBuilder();
foreach (var p in data.parts) sb.Append(p.partId + " pos " + p.xPos + "," + p.yPos + "," + p.zPos + " rot " + p.xRot + "," + p.yRot + "," + p.zRot + "\n");
return sb.ToString();
