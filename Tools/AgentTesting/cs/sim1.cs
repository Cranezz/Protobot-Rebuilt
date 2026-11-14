var c = (GameObject)null; foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None)) if (s.id.StartsWith("CCHL") && s.gameObject.layer == 0) { c = s.gameObject; break; }
var center = c.GetComponent<Renderer>().bounds.center;
var cut = Protobot.Tools.PartCutting.Cut(c, center, c.transform.right);
var cut2 = Protobot.Tools.PartCutting.Cut(c, center, c.transform.forward);
var cut3 = Protobot.Tools.PartCutting.Cut(c, center, c.transform.up);
Protobot.PartsManager.DestroyLoadedObjects();
var data = Protobot.Builds.BuildFile.Read(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/6MD test.pbb");
Protobot.Builds.SceneBuild.GenerateBuild(data);
UnityEditor.EditorApplication.isPaused = false;
return "cuts through middle: right " + cut + " forward " + cut2 + " up " + cut3 + "; loaded 6MD " + data.parts.Length + " frame " + Time.frameCount;
