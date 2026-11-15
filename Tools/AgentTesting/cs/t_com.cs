// Build mode: weight and center of mass (z) of the build, and the heaviest groups ahead of the front axle (z 3).
var total = 0f; var sumz = 0f;
var groups = new System.Collections.Generic.Dictionary<string, (float w, float z)>();
foreach (var p in Protobot.PartsManager.FindLoadedObjects()) {
    if (!p.activeInHierarchy) continue;
    var pn = p.GetComponent<Parts_List.PartName>();
    var w = pn != null ? pn.weightInGrams : 0f;
    var r = p.GetComponent<Renderer>();
    var z = r != null ? r.bounds.center.z : p.transform.position.z;
    total += w; sumz += w * z;
    var id = p.GetComponent<Protobot.SavedObject>().id.Split('-')[0];
    var key = id + (z > 6.2f ? " front" : "");
    groups.TryGetValue(key, out var g); groups[key] = (g.w + w, g.z + w * z);
}
var sb = new System.Text.StringBuilder("total " + (total / 453.6f).ToString("F2") + " lb, COM z " + (sumz / total).ToString("F2") + "\n");
foreach (var kv in groups) sb.AppendLine(kv.Key + " " + (kv.Value.w / 453.6f).ToString("F2") + " lb at z " + (kv.Value.z / Mathf.Max(1f, kv.Value.w)).ToString("F1"));
return sb.ToString();
