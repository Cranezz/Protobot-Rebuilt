// (Play) Opens the polycarbonate editor, sketches on the first part's top face and prints the camera (Temp/sketchcam.txt too).
var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
Protobot.PartsManager.DestroyLoadedObjects();
var data = Protobot.Builds.BuildFile.Read(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Sample Builds/Override Demo Robot.pbb");
Protobot.Builds.SceneBuild.GenerateBuild(data);
UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
Protobot.Polycarb.SheetEditor.Open();
var ed = UnityEngine.Object.FindFirstObjectByType<Protobot.Polycarb.SheetEditor>();
var bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var t = typeof(Protobot.Polycarb.SheetEditor);
var part = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None).Select(s => s.gameObject).First(g => g.GetComponent<Renderer>() != null && g.activeInHierarchy);
var r = part.GetComponent<Renderer>();
t.GetMethod("EnterPlane", bf).Invoke(ed, new object[] { part, r.bounds.center, Vector3.up });
var cam = Protobot.PivotCamera.Main;
return "part " + part.name + " dist " + cam.focusDistance + " focus " + cam.focusPosition + " zoomSens " + Protobot.MousePivotCameraInput.ZoomSens + " panSens " + Protobot.MousePivotCameraInput.PanSens;
