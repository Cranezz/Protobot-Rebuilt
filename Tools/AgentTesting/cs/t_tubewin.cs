GameObject f1 = null;
foreach (var s0 in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None)) if (s0.id == "VXPNEUMATICS-Elbow Fitting") f1 = s0.gameObject;
var m = GameObject.Find("Primary Selection Manager").GetComponent<Protobot.SelectionSystem.SelectionManager>();
m.ClearCurrent();
var port = Protobot.Motion.TubeTool.PortNear(f1, f1.transform.position);
Protobot.Motion.TubeTool.Begin(port);
return "port " + (port != null) + " active " + Protobot.Motion.TubeTool.Active;
