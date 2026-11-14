var tube = Protobot.Motion.PneumaticTube.All[0];
var m = GameObject.Find("Primary Selection Manager").GetComponent<Protobot.SelectionSystem.SelectionManager>();
m.SetCurrent(new Protobot.SelectionSystem.ObjectSelection { gameObject = tube.gameObject });
// the mouse on the tube between two points: a gray dot should show
var line = tube.Line;
var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
pt.GetMethod("PointAt", new[] { typeof(Vector3) }).Invoke(null, new object[] { line[line.Count / 3] });
return "selected " + Protobot.Tools.SelectTool.SelectedPart + " tool " + Protobot.Interface.ToolRail.Current;
