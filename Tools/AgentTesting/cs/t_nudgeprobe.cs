var n = UnityEngine.Object.FindObjectsByType<Protobot.Tools.ArrowNudge>(FindObjectsInactive.Include, FindObjectsSortMode.None);
var sel = Protobot.Tools.SelectTool.SelectedPart;
var mm = UnityEngine.Object.FindObjectsByType<Protobot.MovementManager>(FindObjectsSortMode.None).Select(m => m.MovingObj == null ? "null" : m.MovingObj.name);
return "nudges " + n.Length + " active " + string.Join(",", n.Select(x => x.isActiveAndEnabled)) + " kb " + UnityEngine.InputSystem.Keyboard.current?.name + " tool " + Protobot.Interface.ToolRail.Current + " sel " + (sel == null ? "null" : sel.name) + " movers " + string.Join(",", mm) + " placing " + Protobot.Placement.IsPlacing + " popup " + Protobot.Interface.Popup.IsOpen;
