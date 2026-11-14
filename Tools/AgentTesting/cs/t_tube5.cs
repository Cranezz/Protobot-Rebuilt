var tube = Protobot.Motion.PneumaticTube.All[0];
var line = tube.Line;
var mid = line[line.Count / 4];
var s = Camera.main.WorldToScreenPoint(mid);
return "selected " + Protobot.Tools.SelectTool.SelectedPart + " tool " + Protobot.Interface.ToolRail.Current + " mouse " + Protobot.MouseInput.Position.ToString("F0") + " mid screen " + s.ToString("F0") + " placing " + Protobot.Placement.IsPlacing + " screen " + Screen.width + "x" + Screen.height + " busy " + Protobot.Motion.TubeEditor.Busy + " lines " + line.Count;
