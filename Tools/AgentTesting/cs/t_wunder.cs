GameObject brain = null;
foreach (var s0 in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None)) if (s0.id == "BRAN") brain = s0.gameObject;
var sb = new System.Text.StringBuilder();
foreach (var key in new[] { "1", "5", "12", "20", "21", "A", "B", "H" }) {
  var port = Protobot.Motion.WirePorts.Of(brain).Find(p => p.label == "Port " + key);
  var ray = new Ray(port.Position + port.Out * 3f + Vector3.up * 0.6f, (port.Position - (port.Position + port.Out * 3f + Vector3.up * 0.6f)).normalized);
  var hit = Protobot.Motion.WirePorts.Under(ray, port.kind);
  RaycastHit h; var first = Physics.Raycast(ray, out h, 20f) ? h.collider.name + " at " + (h.point - port.Position).magnitude.ToString("F2") : "nothing";
  sb.Append(key + " -> " + (hit != null ? hit.label : "none") + " (first hit " + first + ")\n");
}
return sb.ToString();
