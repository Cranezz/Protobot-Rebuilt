// (Play, build mode) String phase 1. Temp/str.txt: "rig" (parts to string), "make A,B,C" (a string through parts by
// their names in the rig: posts found by a ray at each part from the +Z side), "info", "look NAME" (camera + picture),
// "tool" (opens the String tool), "select" (selects the string), "clear".
var cmd = System.IO.File.ReadAllText("Temp/str.txt").Trim();
var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
System.Func<string, GameObject> Named = n => { var g = GameObject.Find("strtest " + n); return g; };
if (cmd == "rig" || cmd == "clear") {
    foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None)) if (s.name.StartsWith("strtest") || s.GetComponent<Protobot.Motion.StringLine>() != null) UnityEngine.Object.Destroy(s.gameObject);
    if (cmd == "clear") return "cleared";
    var made = new System.Text.StringBuilder();
    System.Action<string, string, Vector3, Quaternion> Make = (name, id, p, r) => {
        var g = Protobot.PartsManager.GeneratePart(id, p, r);
        if (g == null) { made.Append(name + ": no " + id + "\n"); return; }
        g.name = "strtest " + name;
        made.Append(name + " ok\n");
    };
    var face = Quaternion.Euler(90f, 0f, 0f);   // (pulleys and the drum: axis toward the camera)
    Make("post1", "SNDF-2.00in", new Vector3(-6f, 1f, 0f), Quaternion.LookRotation(Vector3.forward));
    Make("pulley", "VXMOTIONPARTS-Pulley", new Vector3(-1f, 7f, 0f), face);
    Make("pulley2", "VXMOTIONPARTS-Pulley", new Vector3(3f, 7f, 0f), face);
    Make("post2", "SNDF-2.00in", new Vector3(1f, 2f, 0f), Quaternion.LookRotation(Vector3.forward));
    Make("drum", "VXADVANCEDGEARS-Winch Drum", new Vector3(7f, 2f, 0f), face);
    pt.GetMethod("Frames").Invoke(null, new object[] { 3 });
    return made.ToString();
}
if (cmd.StartsWith("make ")) {
    var names = cmd.Substring(5).Split(',');
    var nodes = new System.Collections.Generic.List<Protobot.Motion.StringNode>();
    var sb = new System.Text.StringBuilder();
    foreach (var n in names) {
        var g = Named(n.Trim());
        if (g == null) return "no part " + n;
        var b = g.GetComponent<Renderer>().bounds;
        var ray = new Ray(b.center + new Vector3(0.05f, 0.05f, 10f), Vector3.back);
        if (!Protobot.Motion.StringUi.PostAt(ray, out var post)) return "no post at " + n;
        sb.AppendLine(n + ": " + Protobot.Motion.StringUi.What(post, false) + " r " + post.radius.ToString("F3") + " axis " + post.Axis.ToString("F2") + " part " + (post.part ? post.part.name : "-"));
        nodes.Add(new Protobot.Motion.StringNode { post = post });
    }
    Protobot.Motion.StringUi.MarkSpools(nodes);
    var kind = Protobot.Motion.StringKind.Of("VEX");
    var path = Protobot.Motion.StringLine.PathOf(nodes, kind, 3f);
    if (!path.Valid) return sb + "path problem: " + path.problem;
    var gen = Protobot.PartsManager.GetPartType("STRG").GetComponent<Protobot.PartGenerator>();
    gen.param1.value = "VEX";
    var obj = gen.Generate(nodes[0].post.Center, Quaternion.identity);
    var line = obj.GetComponent<Protobot.Motion.StringLine>();
    line.Set(nodes, path.tight + 1f, 3f);
    sb.AppendLine("made: tight " + path.tight.ToString("F2") + " ways " + string.Join(",", path.ways) + " wound " + path.wound.ToString("F2"));
    return sb.ToString();
}
if (cmd == "info") {
    var sb = new System.Text.StringBuilder();
    foreach (var l in Protobot.Motion.StringLine.All) sb.AppendLine(l.name + " id " + l.GetComponent<Protobot.SavedObject>().id + " token " + Protobot.Motion.Dynamics.Token(l.gameObject, "string") + "\n  " + l.Describe() + "\n  grams " + l.Grams.ToString("F2") + " line pts " + l.Line.Count);
    return sb.ToString();
}
if (cmd.StartsWith("shot ")) {
    pt.GetMethod("Frames").Invoke(null, new object[] { 2 });
    pt.GetMethod("Screenshot").Invoke(null, new object[] { cmd.Substring(5) });
    pt.GetMethod("Frames").Invoke(null, new object[] { 2 });
    return "shot";
}
if (cmd == "from") { Protobot.Motion.StringTool.BeginFrom(Named("pulley")); return "tool from pulley " + Protobot.Motion.StringTool.Active; }
if (cmd == "stoptool") { Protobot.Motion.StringTool.Stop(); return "stopped"; }
if (cmd.StartsWith("look ")) {
    Protobot.PivotCamera.Main.SetTransform(new Vector3(0.5f, 4f, 0f), new Vector3(8f, 180f, 0f), -22f);
    pt.GetMethod("Frames").Invoke(null, new object[] { 3 });
    pt.GetMethod("Screenshot").Invoke(null, new object[] { cmd.Substring(5) });
    pt.GetMethod("Frames").Invoke(null, new object[] { 2 });
    return "shot";
}
if (cmd == "tool") { Protobot.Motion.StringTool.Begin("VEX"); return "tool " + Protobot.Motion.StringTool.Active; }
if (cmd == "select") {
    Protobot.Motion.StringEditor.Select(Protobot.Motion.StringLine.All[0]);
    pt.GetMethod("Frames").Invoke(null, new object[] { 3 });
    return "selected " + (Protobot.Tools.SelectTool.SelectedPart ? Protobot.Tools.SelectTool.SelectedPart.name : "nothing");
}
if (cmd == "tight") {
    var l = Protobot.Motion.StringLine.All[0];
    l.length = l.Path.tight; l.Refresh(true); l.Save();
    var best = l.Line[0];
    foreach (var q in l.Line) if (Mathf.Abs(q.x - 1f) < Mathf.Abs(best.x - 1f) && q.y > 5f) best = q;
    return "tight " + l.Path.tight.ToString("F2") + "; between the pulleys at x " + best.x.ToString("F2") + " y " + best.y.ToString("F3") + " (pulley tops 7 + groove " + l.nodes[1].post.radius.ToString("F3") + " + string 0.0625)";
}
return "?";
