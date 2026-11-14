var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>()?.onClick.Invoke();
Protobot.PartsManager.DestroyLoadedObjects();
var ch = Protobot.PartsManager.GeneratePart("CCHL-1x2-15", new Vector3(0f, 0.5f, 0f), Quaternion.identity);
Protobot.Interface.ToolRail.Select("Transform");
var mgr = GameObject.Find("Primary Selection Manager").GetComponent<Protobot.SelectionSystem.SelectionManager>();
mgr.SetCurrent(new Protobot.SelectionSystem.ObjectSelection { gameObject = ch });
Protobot.PivotCamera.Main.MoveFocusPosition(ch.GetComponent<Renderer>().bounds.center);
UnityEditor.EditorApplication.isPaused = false;
return "pos " + ch.transform.position.ToString("F3");
