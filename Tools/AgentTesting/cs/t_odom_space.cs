// (Play, 6-motor drive loaded) Parts inside the frame (|x| < 5.8) and their boxes: where the tracking wheels can go.
var sb = new System.Text.StringBuilder();
var all = new Bounds(); var first = true;
foreach (var so in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None)) {
    var rs = so.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) continue;
    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
    if (first) { all = b; first = false; } else all.Encapsulate(b);
    if (b.min.x > 5.8f || b.max.x < -5.8f) continue;
    if (so.id.StartsWith("SCRW") || so.id.StartsWith("NUT")) continue;
    sb.AppendLine(so.id + "  x " + b.min.x.ToString("F2") + ".." + b.max.x.ToString("F2") + "  y " + b.min.y.ToString("F2") + ".." + b.max.y.ToString("F2") + "  z " + b.min.z.ToString("F2") + ".." + b.max.z.ToString("F2"));
}
sb.Insert(0, "all " + all.min.ToString("F2") + " .. " + all.max.ToString("F2") + "\n");
return sb.ToString();
