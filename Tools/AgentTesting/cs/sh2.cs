var sb = new System.Text.StringBuilder();
foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.Screw>(FindObjectsSortMode.None)) {
  var parts = s.GetAttachedParts();
  sb.Append(s.name + " connects " + s.CanFormConnection + " attached " + (parts == null ? "none" : string.Join(",", parts.ConvertAll(p => p.name))) + " neighbours " + string.Join(",", System.Linq.Enumerable.Select(Protobot.GroupManager.Neighbours(s.gameObject), g => g.name)) + "\n");
}
return sb.ToString();
