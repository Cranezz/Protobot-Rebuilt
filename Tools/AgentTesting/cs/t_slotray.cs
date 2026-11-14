// From the current camera: for each +z/-z brain port, how far the ray through its screen point first hits from the port (0 = visible).
GameObject brain = null;
foreach (var s0 in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None)) if (s0.id == "BRAN") brain = s0.gameObject;
var cam = Camera.main; var sb = new System.Text.StringBuilder();
foreach (var p in Protobot.Motion.WirePorts.Of(brain)) {
  if (p.number > 5 && p.kind == Protobot.Motion.WireKind.Smart && p.number != 21) continue;
  var ray = new Ray(cam.transform.position, (p.Position - cam.transform.position).normalized);
  RaycastHit h; var hit = Physics.Raycast(ray, out h, 100f, LayerMask.GetMask("Default")) ? (h.point - p.Position).magnitude.ToString("F2") + " " + h.collider.name : "none";
  sb.Append(p.label + " " + p.Position.ToString("F2") + " first hit " + hit + "\n");
}
return sb.ToString();
