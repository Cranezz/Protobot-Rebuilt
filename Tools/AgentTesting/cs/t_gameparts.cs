// Adds test parts to the build (Temp/gameparts.txt: "float" a loose C-channel far away, "pin" a pin on the robot's top,
// "pin2" a second one) before t_gameopen.cs. Names them GameTest so t_gamepartsclear.cs removes them.
var what = System.IO.File.ReadAllText("Temp/gameparts.txt").Trim();
var g = Protobot.Game.SkillsGame.Current; if (g != null) g.Exit();
var all = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None).Where(s => s.gameObject.activeInHierarchy).ToList();
var b = new Bounds(); var first = true;
foreach (var s in all) foreach (var r in s.GetComponentsInChildren<MeshRenderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
var made = new System.Collections.Generic.List<string>();
// the highest part's box: the pins rest on it
var top = all.SelectMany(s => s.GetComponentsInChildren<MeshRenderer>()).OrderByDescending(r => r.bounds.max.y).First().bounds;
foreach (var w in what.Split(',')) {
    GameObject p = null;
    if (w == "float") p = Protobot.PartsManager.GeneratePart("CCHL-1x2-10", b.center + Vector3.right * 40f, Quaternion.identity);
    if (w == "pin" || w == "pin2") {
        var pinType = Protobot.PartsManager.partTypes.First(t => t.id == "OVPN").GetComponent<Protobot.PartGenerator>();
        var opts = pinType.GetParam1Options();
        pinType.param1.value = opts.First(o => o.Contains("Red") && o.Contains("Yellow"));
        p = pinType.Generate(new Vector3(top.center.x + (w == "pin2" ? 0.6f : -0.6f), b.max.y + 1.6f, top.center.z), Quaternion.identity);
    }
    if (p != null && w.StartsWith("pin")) {   // resting on the robot's top
        UnityEngine.Physics.SyncTransforms();
        var pb = p.GetComponentsInChildren<Renderer>()[0].bounds; foreach (var r in p.GetComponentsInChildren<Renderer>()) pb.Encapsulate(r.bounds);
        p.transform.position += Vector3.up * (b.max.y - 0.02f - pb.min.y);
    }
    if (p != null) { p.name = "GameTest " + p.name; made.Add(p.GetComponent<Protobot.SavedObject>().id); }
}
return "made " + string.Join(", ", made);
