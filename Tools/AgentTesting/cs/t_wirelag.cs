// (Play, a build with a Brain and motors loaded, e.g. cs/t_loaddemo.cs) Times what placing a Smart Cable costs each
// frame: "setup" wires the motors to the Brain (9 cables at most, as the bug report's build), "time" starts the cable
// tool from a free brain port and times its parts per call (ms): WirePorts.Under, TubeTool.Pick, the preview mesh, and
// every placed cable's per-frame check (PneumaticTube.LateUpdate). Temp/wirelag.txt: setup | time.
var cmd = System.IO.File.Exists("Temp/wirelag.txt") ? System.IO.File.ReadAllText("Temp/wirelag.txt").Trim() : "time";
var sb = new System.Text.StringBuilder();
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public;
var brain = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None).FirstOrDefault(s => Protobot.Motion.WirePorts.IsBrain(s.gameObject));
if (brain == null) return "no brain";
var bp = Protobot.Motion.WirePorts.Of(brain.gameObject);
var place = typeof(Protobot.Motion.TubeTool).GetMethod("Place", flags);
if (cmd == "setup") {
    var motors = UnityEngine.Object.FindObjectsByType<Protobot.Motion.MotorDrive>(FindObjectsSortMode.None).Take(9).ToList();
    var n = 0;
    foreach (var m in motors) {
        var mp = Protobot.Motion.WirePorts.Of(m.gameObject);
        if (mp.Count == 0) continue;
        var from = bp.Find(p => p.number == n + 1 && p.kind == Protobot.Motion.WireKind.Smart);
        Protobot.Motion.TubeTool.BeginWire(Protobot.Motion.WireKind.Smart, from);
        var tool = UnityEngine.Object.FindFirstObjectByType<Protobot.Motion.TubeTool>();
        var mid = (from.Position + mp[0].Position) / 2f + Vector3.up * 3f;
        place.Invoke(tool, new object[] { new Protobot.Motion.TubeTarget { valid = true, kind = Protobot.Motion.TubeNode.Point, point = mid, what = "air" } });
        place.Invoke(tool, new object[] { new Protobot.Motion.TubeTarget { valid = true, kind = Protobot.Motion.TubeNode.Port, part = m.gameObject, point = mp[0].Position, dir = mp[0].Out, depth = Protobot.Motion.Wire.PortDepth(Protobot.Motion.WireKind.Smart), what = "motor" } });
        n++;
    }
    return "cables " + Protobot.Motion.Wire.Wires.Count;
}
var cam = Camera.main;
var free = bp.Find(p => p.number == 15 && p.kind == Protobot.Motion.WireKind.Smart);
Protobot.Motion.TubeTool.BeginWire(Protobot.Motion.WireKind.Smart, free);
var t = UnityEngine.Object.FindFirstObjectByType<Protobot.Motion.TubeTool>();
var sw = new System.Diagnostics.Stopwatch();
var rays = new System.Collections.Generic.List<Ray>();
var parts = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None).Where(s => s.GetComponent<Renderer>() != null).Take(40).ToList();
foreach (var p in parts) rays.Add(new Ray(cam.transform.position, (p.GetComponent<Renderer>().bounds.center - cam.transform.position).normalized));
sw.Start(); foreach (var r in rays) Protobot.Motion.WirePorts.Under(r, Protobot.Motion.WireKind.Smart); sw.Stop();
sb.AppendLine("WirePorts.Under " + (sw.Elapsed.TotalMilliseconds / rays.Count).ToString("F2") + " ms");
// FeaturePicker on the parts the rays hit (what Pick does for a point on a part)
sw.Reset(); sw.Start(); foreach (var p in parts) Protobot.Interface.FeaturePicker.Pick(p.gameObject); sw.Stop();
sb.AppendLine("FeaturePicker.Pick " + (sw.Elapsed.TotalMilliseconds / parts.Count).ToString("F2") + " ms");
var draw = typeof(Protobot.Motion.TubeTool).GetMethod("Draw", flags);
var targetField = typeof(Protobot.Motion.TubeTool).GetField("target", flags);
targetField.SetValue(t, new Protobot.Motion.TubeTarget { valid = true, kind = Protobot.Motion.TubeNode.Point, point = free.Position + Vector3.up * 4f + Vector3.forward * 3f, what = "air" });
sw.Reset(); sw.Start(); for (var i = 0; i < 20; i++) draw.Invoke(t, new object[] { cam }); sw.Stop();
sb.AppendLine("TubeTool.Draw (preview) " + (sw.Elapsed.TotalMilliseconds / 20).ToString("F2") + " ms");
var late = typeof(Protobot.Motion.PneumaticTube).GetMethod("LateUpdate", flags);
var wires = Protobot.Motion.Wire.Wires.ToList();
sw.Reset(); sw.Start(); for (var i = 0; i < 20; i++) foreach (var w in wires) late.Invoke(w, null); sw.Stop();
sb.AppendLine("placed cables' LateUpdate, all " + wires.Count + ": " + (sw.Elapsed.TotalMilliseconds / 20).ToString("F2") + " ms");
var mesh = new Mesh();
var line = new System.Collections.Generic.List<Vector3> { free.Position, free.Position + Vector3.up * 3f, free.Position + Vector3.up * 3f + Vector3.forward * 6f };
sw.Reset(); sw.Start(); for (var i = 0; i < 20; i++) Protobot.Motion.Wire.BuildMesh(mesh, line, Protobot.Motion.WireKind.Smart, line[0], line[0], Vector3.down, Vector3.forward, null, Vector3.zero, Vector3.zero, true, false); sw.Stop();
sb.AppendLine("Wire.BuildMesh " + (sw.Elapsed.TotalMilliseconds / 20).ToString("F2") + " ms, " + mesh.vertexCount + " verts");
UnityEngine.Object.Destroy(mesh);
t.GetType().GetMethod("Stop", flags)?.Invoke(t, null);
return sb.ToString();
