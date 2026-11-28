// (Play, a game open) The Loader (build 88). Temp/loader.txt: "raise" | "lower" | "look N SIDE" (camera on red Loader N
// from the field: SIDE front/side/back) | "shot NAME" | "drop KIND" (a Match Load into the chosen Loader, sandbox running) | "where"
var cmd = System.IO.File.ReadAllText("Temp/loader.txt").Trim();
var g = Protobot.Game.SkillsGame.Current;
if (g == null) return "no game";
var f = g.field;
var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
if (cmd == "raise" || cmd == "lower") { f.SetLoaderRaised(g.loader, g.Red, cmd == "raise"); return cmd + " " + f.LoaderRaised(g.loader, g.Red); }
if (cmd == "toggle") { g.ToggleLoader(); return "up " + g.LoaderUp; }
if (cmd.StartsWith("look")) {
    var w = cmd.Split(' ');
    var n = w.Length > 1 ? int.Parse(w[1]) : g.loader;
    Transform tube = null;
    var list = new System.Collections.Generic.List<Transform>();
    foreach (var t in f.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("276-9250-031") && t.name.Contains(g.Red ? "Red" : "Blue") && t.GetComponent<MeshRenderer>() != null) list.Add(t);
    tube = list[Mathf.Min(n, list.Count - 1)];
    var b = tube.GetComponent<MeshRenderer>().bounds;
    var c = new Vector3(b.center.x, f.transform.position.y + 9f, b.center.z);
    var inward = (f.transform.position - c); inward.y = 0; inward.Normalize();
    var side = w.Length > 2 ? w[2] : "front";
    var dir = side == "side" ? Quaternion.Euler(0, 60, 0) * inward : side == "back" ? -inward : Quaternion.Euler(0, 25, 0) * inward;
    var yaw = Quaternion.LookRotation(-dir).eulerAngles.y;
    Protobot.PivotCamera.Main.SetTransform(c, new Vector3(18f, yaw, 0f), -42f);
    pt.GetMethod("Frames").Invoke(null, new object[] { 2 });
    return "looking at " + tube.name + " " + b.center.ToString("F1");
}
if (cmd.StartsWith("shot ")) { var r = pt.GetMethod("Screenshot").Invoke(null, new object[] { cmd.Substring(5) }); return r.ToString(); }
if (cmd.StartsWith("drop ")) { g.DropMatchLoad(cmd.Substring(5)); return "dropped"; }
if (cmd == "where") {
    var s = "";
    foreach (var body in Protobot.Motion.Sandbox.Instance.Root.GetComponentsInChildren<Rigidbody>())
        if (body.gameObject.layer == Protobot.Motion.Sandbox.ElementLayer && body.position.y - f.transform.position.y < 30f) {
            var p = f.ToField(body.position);
            var lp = f.LoaderDrop(g.loader, g.Red);
            if (Vector2.Distance(p, f.ToField(lp)) < 6f) s += body.name + " up " + (body.position.y - f.transform.position.y).ToString("F1") + " tilt " + Vector3.Angle(body.transform.up, Vector3.up).ToString("F0") + "\n";
        }
    return s == "" ? "nothing at the loader" : s;
}
return "unknown";
