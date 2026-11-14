var sb = new System.Text.StringBuilder();
foreach (var c in UnityEngine.Object.FindObjectsByType<Protobot.Motion.PneumaticCylinder>(FindObjectsSortMode.None)) {
  sb.Append("cylinder " + c.name + " rod parent " + (c.Rod != null && c.Rod.parent != null ? c.Rod.parent.name : "-"));
  var th = c.Rod != null ? c.Rod.GetComponentInChildren<Protobot.Motion.RodThread>(true) : null;
  sb.Append(" thread " + (th != null ? th.name + " parent " + th.transform.parent.name : "none") + "\n  connected:");
  foreach (var o in Protobot.ConnectedObjExtensions.GetConnectedObjects(c.gameObject, true)) sb.Append(" " + o.name.Replace("(Clone)", "") + "[" + (o.transform.parent != null ? o.transform.parent.name.Replace("(Clone)", "") : "root") + "]");
  sb.Append("\n");
}
return sb.ToString();
