var sb = new System.Text.StringBuilder();
var all = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None).Where(s => s.gameObject.activeInHierarchy && s.transform.position.x < -5f && Mathf.Abs(s.transform.position.z + 5.5f) < 1.3f).ToList();
foreach (var s in all.Where(s => s.id.StartsWith("OMNI") || s.id == "SCRW-1.75in" || s.id.StartsWith("GEAR"))) {
    var nb = Protobot.GroupManager.Neighbours(s.gameObject).Select(n => n == null ? "(destroyed)" : (n.GetComponent<Protobot.SavedObject>()?.id ?? n.name) + (n.activeInHierarchy ? "" : "(hidden)"));
    sb.AppendLine(s.id + " @" + s.transform.position.z.ToString("0.0") + " neighbours: " + string.Join(", ", nb));
    
}
return sb.ToString();
