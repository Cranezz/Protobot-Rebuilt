// (Play, video 2 target loaded) Heights and holes for tutorial 5's corner pieces.
var sb = new System.Text.StringBuilder();
foreach (var so in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None)) {
    var r = so.GetComponent<Renderer>(); if (r == null) continue;
    var b = r.bounds;
    if (so.id.StartsWith("CCHL") && b.center.x < 0 || so.id.StartsWith("OMNI") && b.center.x < 0 && b.center.z < -5)
        sb.AppendLine(so.id + " bounds min " + b.min.ToString("0.000") + " max " + b.max.ToString("0.000"));
}
// top-flange holes of the left rails near the back corner
foreach (var so in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None)) {
    if (!so.id.StartsWith("CCHL-1x2-31") || so.transform.position.x > 0) continue;
    var tops = so.GetComponentsInChildren<Protobot.HoleCollider>().Where(h => Mathf.Abs(h.transform.forward.y) > 0.9f && h.transform.position.y > 2f && h.transform.position.z < -3f).OrderBy(h => h.transform.position.z).Select(h => h.transform.position.ToString("0.000"));
    sb.AppendLine("rail at x " + so.transform.position.x.ToString("0.00") + " top holes: " + string.Join(" ", tops));
}
var t = Protobot.PartsManager.GetPartType("VXSPACERS");
if (t != null) sb.AppendLine("spacers: " + string.Join(" | ", t.GetComponent<Protobot.PartGenerator>().GetParam1Options().Where(o => o.Contains("5/16"))));
return sb.ToString();
