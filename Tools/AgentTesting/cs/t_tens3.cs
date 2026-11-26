// (Play) every shaft's neighbours
var sb = new System.Text.StringBuilder();
foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None)) {
    if (!s.id.StartsWith("SHFT")) continue;
    sb.AppendLine(s.id + " at " + s.transform.position.ToString("F2") + ": " + string.Join(", ", Protobot.GroupManager.Neighbours(s.gameObject).Select(n => n.name)));
}
return sb.ToString();
