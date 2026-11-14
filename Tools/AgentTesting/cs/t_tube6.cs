// One mouse step on the selected tube, from Temp/step.txt: "hover", "press", "drag", "release".
var step = System.IO.File.ReadAllText("Temp/step.txt").Trim();
var tube = Protobot.Motion.PneumaticTube.All[0];
var mouse = UnityEngine.InputSystem.Mouse.current;
var line = tube.Line;
if (step == "hover") {
  var best = line[0]; var bestD = -1f;
  foreach (var q in line) {
    var near = float.MaxValue;
    foreach (var n in tube.nodes) near = Mathf.Min(near, Vector3.Distance(q, n.Position(tube.transform)));
    if (near > bestD) { bestD = near; best = q; }
  }
  System.IO.File.WriteAllText("Temp/mid.txt", best.x + "," + best.y + "," + best.z);
}
var p = System.IO.File.ReadAllText("Temp/mid.txt").Split(',');
var world = new Vector3(float.Parse(p[0]), float.Parse(p[1]), float.Parse(p[2]));
if (step == "drag" || step == "release") world += new Vector3(0f, 1.5f, 0.5f);
var screen = Camera.main.WorldToScreenPoint(world);
var down = step == "press" || step == "drag";
UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new UnityEngine.InputSystem.LowLevel.MouseState { position = new Vector2(screen.x, screen.y), buttons = (ushort)(down ? 1 : 0) });
return step + ": nodes " + tube.nodes.Count + " length " + tube.Length.ToString("F2") + " busy " + Protobot.Motion.TubeEditor.Busy + " editing " + tube.editing + " selected " + Protobot.Tools.SelectTool.SelectedPart;
