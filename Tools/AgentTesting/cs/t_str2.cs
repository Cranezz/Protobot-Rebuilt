// (Play, the t_str1 rig made) Temp/str2.txt: "tool" clicks through the String tool (Press by reflection: post1, pulley,
// pulley again (other side), pulley2, drum, drum again = tie off), "move" lifts pulley2 2", "undo", "roundtrip" saves the
// build to build data and opens it again, "sim" / "stop" Simulate, "count".
var cmd = System.IO.File.ReadAllText("Temp/str2.txt").Trim();
var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
System.Func<string> Count = () => {
    var s = "strings " + Protobot.Motion.StringLine.All.Count(l => l != null && l.gameObject.layer != 12);
    foreach (var l in Protobot.Motion.StringLine.All) if (l != null && l.gameObject.layer != 12) s += "\n  " + l.Describe().Replace("\n", " | ") + " | nodes " + l.nodes.Count + " ways " + (l.Path != null ? string.Join(",", l.Path.ways) : "-");
    return s;
};
System.Func<string, Protobot.Motion.BandPost> Post = n => {
    var g = GameObject.Find("strtest " + n); var b = g.GetComponent<Renderer>().bounds;
    Protobot.Motion.StringUi.PostAt(new Ray(b.center + new Vector3(0.05f, 0.05f, 10f), Vector3.back), out var p); return p;
};
if (cmd == "count") return Count();
if (cmd == "tool") {
    Protobot.Motion.StringTool.Begin("PARA");
    var tool = UnityEngine.Object.FindAnyObjectByType<Protobot.Motion.StringTool>();
    var press = typeof(Protobot.Motion.StringTool).GetMethod("Press", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    var sb = new System.Text.StringBuilder();
    foreach (var n in new[] { "post1", "pulley", "pulley2", "pulley", "drum", "drum" }) {
        press.Invoke(tool, new object[] { Post(n) });
        var picks = (System.Collections.IList)typeof(Protobot.Motion.StringTool).GetField("picks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(tool);
        sb.Append(n + " -> picks " + picks.Count + "; ");
    }
    Protobot.Motion.StringTool.Stop();
    return sb + "\n" + Count();
}
if (cmd == "move") {
    var g = GameObject.Find("strtest pulley2"); g.transform.position += Vector3.up * 2f;
    pt.GetMethod("Frames").Invoke(null, new object[] { 2 });
    return Count();
}
if (cmd == "undo") { Protobot.StateSystems.StateSystem.instance.Undo(); pt.GetMethod("Frames").Invoke(null, new object[] { 2 }); return Count(); }
if (cmd == "roundtrip") {
    var data = Protobot.Builds.SceneBuild.ToBuildData();
    var path = System.IO.Path.GetFullPath("Temp/strtest.pbb");
    Protobot.Builds.BuildFile.Write(path, data);
    var back = Protobot.Builds.BuildFile.Read(path);
    Protobot.Builds.SceneBuild.GenerateBuild(back);
    pt.GetMethod("Frames").Invoke(null, new object[] { 4 });
    return "parts " + back.parts.Length + "\n" + Count();
}
if (cmd == "sim") { Protobot.Motion.Sandbox.Begin(); pt.GetMethod("Frames").Invoke(null, new object[] { 4 }); return "running " + Protobot.Motion.Sandbox.Running + "\n" + Count(); }
if (cmd == "stop") { Protobot.Motion.Sandbox.Stop(); return "stopped"; }
return "?";
