// (Play, build mode, demo robot loaded) Temp/cupsim.txt: "place" puts an Override Cup lying on the robot's top, 0.05"
// above the part under it; "sim" starts Simulate; "where" reports the cup's height now (in the simulation) and where
// it started; "stop" stops Simulate and removes the cup.
var cmd = System.IO.File.ReadAllText("Temp/cupsim.txt").Trim();
GameObject cup = GameObject.Find("Override Cup(Clone)");
if (cmd == "place") {
    // The robot's middle top: a ray down from above its middle.
    var parts = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None);
    var b = new Bounds(); var first = true;
    foreach (var p in parts) foreach (var r in p.GetComponentsInChildren<MeshRenderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
    var from = new Vector3(b.center.x + 2f, b.max.y + 20f, b.center.z - 2f);
    if (!Physics.Raycast(from, Vector3.down, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore)) return "no hit";
    if (cup == null) cup = Protobot.PartsManager.GeneratePart("OVCP", Vector3.zero, Quaternion.identity);
    // Lying (axis along X): its radius ~1.6 above the surface.
    var mesh = cup.GetComponent<MeshFilter>().sharedMesh;
    cup.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
    var rb = cup.GetComponent<MeshRenderer>().bounds;
    cup.transform.position += new Vector3(hit.point.x, hit.point.y + 0.05f - rb.min.y, hit.point.z) - new Vector3(rb.center.x, 0f, rb.center.z);
    System.IO.File.WriteAllText("Temp/cupsim_start.txt", cup.GetComponent<MeshRenderer>().bounds.min.y.ToString("F3"));
    return "cup on " + hit.collider.name + " (part " + (hit.collider.GetComponentInParent<Protobot.SavedObject>() ? hit.collider.GetComponentInParent<Protobot.SavedObject>().id : "?") + ") at " + hit.point.ToString("F2");
}
if (cmd == "sim") { Protobot.Motion.Sandbox.Begin(); return "running " + Protobot.Motion.Sandbox.Running; }
if (cmd == "where") {
    if (cup == null) return "no cup";
    var start = System.IO.File.ReadAllText("Temp/cupsim_start.txt");
    var r = cup.GetComponent<MeshRenderer>();
    var now = Protobot.Motion.Sandbox.Running ? Protobot.Motion.Sandbox.SimPoint(cup, r.bounds.center) : r.bounds.center;
    return "cup middle now " + now.y.ToString("F3") + " (bottom started at " + start + ", middle " + r.bounds.center.y.ToString("F3") + ")";
}
if (cmd == "stop") { Protobot.Motion.Sandbox.Stop(); if (cup != null) UnityEngine.Object.Destroy(cup); return "stopped"; }
return "?";
