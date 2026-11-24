var sb = new System.Text.StringBuilder();
foreach (var so in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None)) {
    var r = so.GetComponent<Renderer>(); if (r == null) continue;
    if (so.id.StartsWith("CCHL-1x2-23") || so.id.StartsWith("SNDF") && so.transform.position.x < 0 || so.id.StartsWith("MOTR") && so.transform.position.x < 0) sb.AppendLine(so.id + " " + r.bounds.min.ToString("0.000") + " .. " + r.bounds.max.ToString("0.000"));
}
return sb.ToString();
