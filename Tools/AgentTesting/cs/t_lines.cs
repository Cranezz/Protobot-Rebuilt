var parts = new System.Collections.Generic.HashSet<GameObject>(Protobot.PartsManager.FindLoadedObjects());
var s = "";
foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)) {
  if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
  var t = r.transform; bool inPart = false; while (t != null) { if (parts.Contains(t.gameObject)) { inPart = true; break; } t = t.parent; }
  if (inPart) continue;
  if (r.gameObject.layer == 5) continue;
  s += r.name + " (" + r.GetType().Name + ") layer " + r.gameObject.layer + " parent " + (r.transform.parent ? r.transform.parent.name : "-") + "\n";
}
return s;
