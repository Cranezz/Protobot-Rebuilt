// The first wheel (left, z -5.5): its group, and what each 1.75" bolt near it holds.
var sb = new System.Text.StringBuilder();
var near = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None).Where(s => s.gameObject.activeInHierarchy && s.transform.position.x < -5f && Mathf.Abs(s.transform.position.z + 5.5f) < 1.3f).ToList();
foreach (var s in near.OrderBy(s => s.id)) sb.AppendLine(s.id + " at " + s.transform.position.ToString("0.000") + " rot " + s.transform.eulerAngles.ToString("0"));
foreach (var w in near.Where(s => s.id.StartsWith("OMNI"))) {
    sb.AppendLine("wheel group: " + string.Join(", ", Protobot.ConnectedObjExtensions.GetConnectedObjects(w.gameObject, true, true).Select(o => o.GetComponent<Protobot.SavedObject>()?.id ?? o.name)));
    foreach (var h in w.GetComponentsInChildren<Protobot.HoleCollider>()) sb.AppendLine("  wheel hole at " + h.transform.position.ToString("0.000"));
}
foreach (var b in near.Where(s => s.id == "SCRW-1.75in")) {
    var cp = b.GetComponent<Protobot.ConnectingPart>();
    var holes = cp != null && cp.holeDetector != null ? cp.holeDetector.holes.Where(h => h != null && h.holeData != null).Select(h => h.holeData.part.GetComponent<Protobot.SavedObject>()?.id ?? "?") : new string[0];
    sb.AppendLine("bolt at " + b.transform.position.ToString("0.000") + " holds: " + string.Join(", ", holes));
}
return sb.ToString();
