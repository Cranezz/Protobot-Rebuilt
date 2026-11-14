// Temp/bend.txt: a radius to set on the tube (or "check"); reports length, and the panel.
var arg = System.IO.File.ReadAllText("Temp/bend.txt").Trim();
var tube = Protobot.Motion.PneumaticTube.All[0];
var m = GameObject.Find("Primary Selection Manager").GetComponent<Protobot.SelectionSystem.SelectionManager>();
if (Protobot.Tools.SelectTool.SelectedPart != tube.gameObject) m.SetCurrent(new Protobot.SelectionSystem.ObjectSelection { gameObject = tube.gameObject });
if (arg != "check") {
  tube.bendRadius = float.Parse(arg);
  tube.Refresh(true);
  tube.Save();
}
var panel = GameObject.Find("Tubing Panel");
return "radius " + tube.bendRadius + " length " + tube.Length.ToString("F2") + " line points " + tube.Line.Count + " token " + Protobot.Motion.Dynamics.Token(tube.gameObject, "tubebend") + " panel " + (panel != null);
