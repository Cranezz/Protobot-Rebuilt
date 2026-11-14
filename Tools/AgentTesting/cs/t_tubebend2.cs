// Temp/bend.txt: "set" (radius 3 as one undo step), "undo", or "check": the tube's radius and the panel's label.
var arg = System.IO.File.ReadAllText("Temp/bend.txt").Trim();
var tube = Protobot.Motion.PneumaticTube.All[0];
var m = GameObject.Find("Primary Selection Manager").GetComponent<Protobot.SelectionSystem.SelectionManager>();
if (Protobot.Tools.SelectTool.SelectedPart != tube.gameObject) m.SetCurrent(new Protobot.SelectionSystem.ObjectSelection { gameObject = tube.gameObject });
if (arg == "set") {
  var before = new Protobot.Motion.PneumaticTube.TubeElement(tube);
  tube.bendRadius = 3f; tube.Refresh(true); tube.Save();
  Protobot.StateSystems.StateSystem.AddElement(before);
  Protobot.StateSystems.StateSystem.AddState(new Protobot.Motion.PneumaticTube.TubeElement(tube));
}
if (arg == "undo") Protobot.StateSystems.StateSystem.instance.Undo();
var panel = GameObject.Find("Tubing Panel");
var label = "";
if (panel != null) foreach (var t in panel.GetComponentsInChildren<TMPro.TextMeshProUGUI>()) if (t.text.EndsWith(" R")) label = t.text;
return arg + ": radius " + tube.bendRadius + " length " + tube.Length.ToString("F2") + " slider " + label;
