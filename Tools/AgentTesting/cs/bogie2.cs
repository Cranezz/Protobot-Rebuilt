var type = Protobot.PartsManager.GetPartType("VXWHEELS");
var sb = new System.Text.StringBuilder();
foreach (Transform child in type.GetComponentsInChildren<Transform>(true)) {
  if (!child.name.Contains("Bogie")) continue;
  sb.Append(child.name + ":\n");
  foreach (var h in child.GetComponentsInChildren<Protobot.HoleCollider>(true)) sb.Append("   " + child.InverseTransformPoint(h.transform.position).ToString("F3") + " axis " + child.InverseTransformDirection(h.transform.forward).ToString("F1") + " size " + h.transform.lossyScale.ToString("F3") + "\n");
}
return sb.ToString();
