var launch = UnityEngine.GameObject.Find("Launch Canvas");
if (launch != null) foreach (var b in launch.GetComponentsInChildren<UnityEngine.UI.Button>(true)) if (b.name == "Close Button" && b.transform.parent.name == "Splash Screen") { b.onClick.Invoke(); break; }
Protobot.PartsManager.DestroyLoadedObjects();
Protobot.Builds.SceneBuild.GenerateBuild(Protobot.Builds.BuildFile.Read(System.IO.Path.GetFullPath("Sample Builds/6 Motor Drive (3.25 omni, 360 rpm).pbb")));
return "loaded";
