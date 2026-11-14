// Temp/mouse.txt: "w x y z down" (a world point) or "s x y down" (screen), down 0/1. Reports the bands.
var f = System.IO.File.ReadAllText("Temp/mouse.txt").Trim().Split(' ');
Vector2 screen;
int down;
if (f[0] == "w") { var w = Camera.main.WorldToScreenPoint(new Vector3(float.Parse(f[1]), float.Parse(f[2]), float.Parse(f[3]))); screen = new Vector2(w.x, w.y); down = int.Parse(f[4]); }
else { screen = new Vector2(float.Parse(f[1]), float.Parse(f[2])); down = int.Parse(f[3]); }
UnityEngine.InputSystem.InputSystem.QueueStateEvent(UnityEngine.InputSystem.Mouse.current, new UnityEngine.InputSystem.LowLevel.MouseState { position = screen, buttons = (ushort)down });
var et = UnityEngine.Object.FindFirstObjectByType<Protobot.Motion.ElasticTool>();
var picks = et != null ? (System.Collections.IList)typeof(Protobot.Motion.ElasticTool).GetField("picks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(et) : null;
var sb = new System.Text.StringBuilder("picks " + (picks != null ? picks.Count : -1) + " frame " + Time.frameCount + " tool " + Protobot.Motion.ElasticTool.Active + " busy " + Protobot.Motion.BandEditor.Busy + " sel " + Protobot.Tools.SelectTool.SelectedPart + " | ");
foreach (var b in Protobot.Motion.ElasticBand.All) {
  if (b.gameObject.layer == 12) continue;
  sb.Append("band " + b.kind.key + " x" + b.bands + " wraps " + b.wraps + " posts " + b.posts.Count + " [");
  foreach (var p in b.posts) sb.Append((p.part != null ? p.part.transform.position.ToString("F1") : "none") + (p.inside ? "i" : "") + " ");
  sb.Append("] " + b.Describe() + "; ");
}
return sb.ToString();
