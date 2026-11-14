// Temp/flow.txt: "at x y z" (mouse there), "press" / "shiftpress" (the tool's click on the post under the mouse),
// "strand x y z" (selected band: drag the strand nearest that point and let go where the mouse is),
// "hook i" (drag post i's dot and let go where the mouse is), "click i" (click post i's dot), "select". Reports bands.
var f = System.IO.File.ReadAllText("Temp/flow.txt").Trim().Split(' ');
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var cam = Camera.main;
var sb = new System.Text.StringBuilder();
// The point aimed at (Temp/aim.txt, set by "at"): rays from the camera to it stand for the mouse.
if (f[0] == "at") System.IO.File.WriteAllText("Temp/aim.txt", f[1] + " " + f[2] + " " + f[3]);
var aimF = System.IO.File.Exists("Temp/aim.txt") ? System.IO.File.ReadAllText("Temp/aim.txt").Trim().Split(' ') : new[] { "0", "0", "0" };
var aim = new Vector3(float.Parse(aimF[0]), float.Parse(aimF[1]), float.Parse(aimF[2]));
var ray = new Ray(cam.transform.position, (aim - cam.transform.position).normalized);
System.Func<Protobot.Motion.BandPost> Hover = () => { Protobot.Motion.BandPost p; return Protobot.Motion.ElasticTool.PostAt(ray, out p) ? p : null; };
if (f[0] == "press" || f[0] == "shiftpress") {
  var et = UnityEngine.Object.FindFirstObjectByType<Protobot.Motion.ElasticTool>();
  var h = Hover();
  sb.Append("hover " + (h != null ? h.what : "none") + " ");
  if (h != null) typeof(Protobot.Motion.ElasticTool).GetMethod("Press", flags).Invoke(et, new object[] { h, f[0] == "shiftpress" });
}
Protobot.Motion.ElasticBand band = null;
foreach (var b in Protobot.Motion.ElasticBand.All) if (b.gameObject.layer != 12) band = b;
if (f[0] == "select") Protobot.Motion.BandEditor.Select(band);
if (f[0] == "strand" || f[0] == "hook" || f[0] == "click") {
  var ed = UnityEngine.Object.FindFirstObjectByType<Protobot.Motion.BandEditor>();
  var t = typeof(Protobot.Motion.BandEditor);
  t.GetField("band", flags).SetValue(ed, band);
  if (f[0] == "strand") {
    var at = new Vector3(float.Parse(f[1]), float.Parse(f[2]), float.Parse(f[3]));
    t.GetField("strand", flags).SetValue(ed, (int)t.GetMethod("NearestSpan", flags).Invoke(null, new object[] { band.Shape, at }));
  }
  else t.GetField("hook", flags).SetValue(ed, int.Parse(f[1]));
  t.GetField("pressAt", flags).SetValue(ed, f[0] == "click" ? Protobot.Interface.Overlay.Mouse : new Vector2(-5000f, -5000f));
  t.GetField("previewKey", flags).SetValue(ed, null);
  var h = Hover();
  sb.Append("hover " + (h != null ? h.what : "none") + " ");
  t.GetMethod("Dragging", flags).Invoke(ed, new object[] { cam, ray, h, Protobot.Interface.Overlay.Mouse, false });
}
sb.Append("tool " + Protobot.Motion.ElasticTool.Active + " | ");
foreach (var b in Protobot.Motion.ElasticBand.All) {
  if (b.gameObject.layer == 12) continue;
  sb.Append("band " + b.kind.key + " x" + b.bands + " wraps " + b.wraps + " posts " + b.posts.Count + " [");
  foreach (var p in b.posts) sb.Append((p.part != null ? p.part.transform.position.ToString("F1") : "none") + (p.inside ? "i" : "") + " ");
  sb.Append("] " + b.Describe() + "; ");
}
return sb.ToString();
