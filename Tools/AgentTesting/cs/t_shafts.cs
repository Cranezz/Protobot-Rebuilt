var sb = new System.Text.StringBuilder();
foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None)) {
  if (!s.id.StartsWith("SHFT")) continue;
  var r = s.GetComponent<UnityEngine.Renderer>();
  sb.Append(s.id + " len " + (r != null ? r.bounds.size.ToString("F2") : "?") + " scale " + s.transform.localScale.z.ToString("F1") + " | ");
}
return sb.ToString();
