// (Play) Sketch on a part's "Show only this part" (build 77). Temp/sketchonly.txt: "load PATH", "enter" (opens the
// plastic editor and sketches on the top face of the part nearest the build's middle), "only" (toggles), "count",
// "leave" (back to the sheet), "close" (closes the editor), "shot NAME".
var cmd = System.IO.File.ReadAllText("Temp/sketchonly.txt").Trim();
var E = typeof(Protobot.Polycarb.SheetEditor);
var BF = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
System.Func<string> Count = () => {
    int on = 0, off = 0;
    foreach (var p in Protobot.PartsManager.FindLoadedObjects()) foreach (var r in p.GetComponentsInChildren<Renderer>()) { if (r.enabled) on++; else off++; }
    var ed = Protobot.Polycarb.SheetEditor.Instance;
    var only = ed != null ? (bool)E.GetField("onlyThisPart", BF).GetValue(ed) : false;
    var plane = ed != null ? E.GetField("planePart", BF).GetValue(ed) as GameObject : null;
    var btn = ed != null ? E.GetField("onlyButton", BF).GetValue(ed) as UnityEngine.UI.Button : null;
    return "renderers on " + on + " off " + off + " | only " + only + " | plane " + (plane != null ? plane.name : "-") + " | hidden " + Protobot.Interface.FeaturePicker.Hidden.Count
        + " | button " + (btn != null && btn.gameObject.activeInHierarchy ? btn.GetComponentInChildren<TMPro.TMP_Text>().text : "hidden");
};
if (cmd.StartsWith("load ")) {
    var launch = GameObject.Find("Launch Canvas");
    if (launch != null) foreach (var b in launch.GetComponentsInChildren<UnityEngine.UI.Button>(true)) if (b.name == "Close Button" && b.transform.parent.name == "Splash Screen" && b.gameObject.activeInHierarchy) { b.onClick.Invoke(); break; }
    Protobot.PartsManager.DestroyLoadedObjects();
    var data = Protobot.Builds.BuildFile.Read(cmd.Substring(5));
    Protobot.Builds.SceneBuild.GenerateBuild(data);
    return "loaded " + data.parts.Length;
}
if (cmd == "enter") {
    if (!Protobot.Polycarb.SheetEditor.IsOpen) Protobot.Polycarb.SheetEditor.Open();
    var ed = Protobot.Polycarb.SheetEditor.Instance;
    // the top face of a C-channel: cast down onto the parts
    Physics.SyncTransforms();
    var parts = Protobot.PartsManager.FindLoadedObjects().Where(p => p != null && p.name.Contains("C-Channel") || p.name.StartsWith("CCHN") || p.name.Contains("Channel")).ToList();
    if (parts.Count == 0) parts = Protobot.PartsManager.FindLoadedObjects().ToList();
    foreach (var part in parts) {
        var r = part.GetComponent<Renderer>(); if (r == null) continue;
        var from = r.bounds.center + Vector3.up * 30f;
        foreach (var hit in Physics.RaycastAll(from, Vector3.down, 60f, LayerMask.GetMask("Default")).OrderBy(h => h.distance)) {
            if (Protobot.Interface.FeaturePicker.PartOf(hit.collider.gameObject) != part) break;
            E.GetMethod("EnterPlane", BF).Invoke(ed, new object[] { part, hit.point, hit.normal });
            return "entered on " + part.name + " | " + Count();
        }
    }
    return "no face found among " + parts.Count;
}
if (cmd == "only") {
    var ed = Protobot.Polycarb.SheetEditor.Instance;
    var btn = E.GetField("onlyButton", BF).GetValue(ed) as UnityEngine.UI.Button;
    btn.onClick.Invoke();
    return Count();
}
if (cmd == "leave") { var ed = Protobot.Polycarb.SheetEditor.Instance; E.GetMethod("LeaveInPlace", BF).Invoke(ed, new object[] { false }); return Count(); }
if (cmd == "close") { var ed = Protobot.Polycarb.SheetEditor.Instance; E.GetMethod("Close", BF).Invoke(ed, null); return "closed"; }
if (cmd.StartsWith("shot ")) {
    var t = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
    return (string)t.GetMethod("Screenshot").Invoke(null, new object[] { cmd.Substring(5) });
}
return Count();
