// (Play) Tutorial demo scripts (Builds/DemoPlayer). Temp/demo.txt: "run SCRIPT [FOLDER]" (FOLDER = record frames there),
// "status", "stop", "binds" (the camera's mouse buttons and every original shortcut), "ui PATHPART" (active interface
// objects whose path contains it, with their screen middles in 1920x1080 top-left coordinates).
var cmd = System.IO.File.ReadAllText("Temp/demo.txt").Trim();
if (cmd.StartsWith("run ")) {
    var rest = cmd.Substring(4).Trim();
    string script = rest, folder = null;
    var q = rest.IndexOf('|');
    if (q > 0) { script = rest.Substring(0, q).Trim(); folder = rest.Substring(q + 1).Trim(); }
    Protobot.Builds.DemoPlayer.Run(script, folder);
    return "started " + script + (folder != null ? " -> " + folder : "");
}
if (cmd == "status") return Protobot.Builds.DemoPlayer.Status + " | frame " + Protobot.Builds.FrameRecorder.Frame + " | t " + Time.time.ToString("0.0");
if (cmd == "stop") { Protobot.Builds.DemoPlayer.Stop(); Protobot.Builds.FrameRecorder.Stop(); return "stopped"; }
if (cmd == "binds") {
    var sb = new System.Text.StringBuilder();
    foreach (var e in UnityEngine.Object.FindObjectsByType<Protobot.InputEvents.InputEvent>(FindObjectsInactive.Include, FindObjectsSortMode.None).OrderBy(e => e.name))
        sb.AppendLine(e.name + ": " + e.CurrentKeysText);
    return sb.ToString();
}
if (cmd.StartsWith("ui ")) {
    var part = cmd.Substring(3);
    var sb = new System.Text.StringBuilder();
    float s = 1080f / Screen.height;
    foreach (var r in UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None)) {
        if (!r.gameObject.activeInHierarchy) continue;
        var path = r.name; for (var p = r.parent; p != null; p = p.parent) path = p.name + "/" + path;
        if (!path.Contains(part)) continue;
        var c = new Vector3[4]; r.GetWorldCorners(c); var m = (c[0] + c[2]) / 2f;
        sb.AppendLine(path + "  @ " + (m.x * s).ToString("0") + "," + ((Screen.height - m.y) * s).ToString("0"));
    }
    return sb.ToString();
}
if (cmd == "parts") {
    var sb = new System.Text.StringBuilder();
    foreach (var so in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        sb.AppendLine(so.id + " layer " + LayerMask.LayerToName(so.gameObject.layer) + " active " + so.gameObject.activeInHierarchy + " at " + so.transform.position);
    sb.AppendLine("mouse " + UnityEngine.InputSystem.Mouse.current?.name + " pos " + UnityEngine.InputSystem.Mouse.current?.position.ReadValue() + " left " + UnityEngine.InputSystem.Mouse.current?.leftButton.isPressed);
    return sb.ToString();
}
if (cmd == "holes") {
    var parts = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None).ToList();
    var sb = new System.Text.StringBuilder();
    var cam = Protobot.PivotCamera.Main;
    sb.AppendLine("camera focus " + cam.focusPosition + " angle " + cam.lookAngle + " dist " + cam.focusDistance + " cam at " + Camera.main.transform.position);
    if (parts.Count >= 2) {
        var a = parts[0].GetComponentsInChildren<Protobot.HoleCollider>(); var b = parts[1].GetComponentsInChildren<Protobot.HoleCollider>();
        var best = a.SelectMany(h => b.Select(o => new { h = h, o = o, d = Vector3.Distance(h.transform.position, o.transform.position) })).OrderBy(x => x.d).Take(5);
        foreach (var x in best) sb.AppendLine("d " + x.d.ToString("0.000") + " cross " + Vector3.Cross(x.h.transform.forward, x.o.transform.position - x.h.transform.position).magnitude.ToString("0.000") + " axisdot " + Vector3.Dot(x.h.transform.forward, x.o.transform.forward).ToString("0.00"));
    }
    return sb.ToString();
}
if (cmd == "vholes") {
    var parts = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.InstanceID).ToList();
    var sb = new System.Text.StringBuilder();
    foreach (var so in parts) {
        var hs = so.GetComponentsInChildren<Protobot.HoleCollider>().Where(h => Mathf.Abs(h.transform.forward.y) > 0.9f).ToList();
        sb.AppendLine(so.id + " pos " + so.transform.position + " rot " + so.transform.eulerAngles + " vertical holes " + hs.Count + " y: " + string.Join(",", hs.Select(h => h.transform.position.y.ToString("0.00")).Distinct()));
        foreach (var h in hs.Take(4)) sb.AppendLine("   " + h.transform.position.ToString("F3"));
    }
    return sb.ToString();
}
if (cmd == "dup") {
    var sb = new System.Text.StringBuilder();
    foreach (var e in UnityEngine.Object.FindObjectsByType<Protobot.InputEvents.InputEvent>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(x => x.name == "Duplicate" || x.name == "Multi-Select Button")) {
        sb.AppendLine(e.name + " IsPressed " + e.IsPressed + " action pressed " + e.defaultAction.IsPressed() + " enabled " + e.defaultAction.enabled + " rebind empty " + e.rebindAction.IsEmpty + " rebind pressed " + e.rebindAction.action.IsPressed());
        foreach (var c in e.defaultAction.controls) sb.AppendLine("   default " + c.path + " pressed " + UnityEngine.InputSystem.InputControlExtensions.IsPressed(c) + " device " + c.device.name);
        foreach (var c in e.rebindAction.action.controls) sb.AppendLine("   rebind " + c.path + " pressed " + UnityEngine.InputSystem.InputControlExtensions.IsPressed(c) + " device " + c.device.name);
    }
    return sb.ToString();
}
if (cmd == "gizmo") {
    var sb = new System.Text.StringBuilder();
    float sc = 1080f / Screen.height;
    foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)) {
        var tn = mb.GetType().Name;
        if (!(tn.Contains("PositionTool") || tn.Contains("RotationTool") || tn.Contains("ClickDrag") || tn.Contains("PositionAxis") || tn.Contains("PositionPlane") || tn.Contains("RotateRing"))) continue;
        if (!mb.isActiveAndEnabled) continue;
        var path = mb.name; for (var p = mb.transform.parent; p != null; p = p.parent) path = p.name + "/" + path;
        var r = mb.GetComponent<Renderer>(); var w = r != null ? r.bounds.center : mb.transform.position;
        var sp = Camera.main.WorldToScreenPoint(w);
        sb.AppendLine(tn + " " + path + " @ " + (sp.x * sc).ToString("0") + "," + ((Screen.height - sp.y) * sc).ToString("0") + " renderer " + (r != null && r.enabled));
    }
    return sb.ToString();
}
return "?";
