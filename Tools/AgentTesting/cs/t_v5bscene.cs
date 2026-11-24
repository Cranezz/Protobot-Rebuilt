// (Play) Tutorial 5's start: tutorial 2's drivetrain, whole.
var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
UnityEngine.PlayerPrefs.SetInt("V2.Sketch.OnlyThisPart", 0);
Protobot.PartsManager.DestroyLoadedObjects();
Protobot.Builds.DemoPlayer.Target.Clear();
var data = Protobot.Builds.BuildFile.Read("C:/Dev/ProtobotRebuiltV2/Video/tutorials/02-drivetrain/target.pbb");
Protobot.Builds.SceneBuild.GenerateBuild(data);
return "parts " + data.parts.Length;
