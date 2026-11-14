Protobot.Motion.Sandbox.Begin();
var sb = new System.Text.StringBuilder();
foreach (var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) {
  if (rb.name == "Body") rb.isKinematic = true;
  sb.Append(rb.name + " pos " + rb.position.ToString("F3") + " com " + rb.worldCenterOfMass.ToString("F3") + "\n");
}
return sb.ToString();
