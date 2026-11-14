var sb = new System.Text.StringBuilder();
foreach (var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
  if (rb.name == "Body" || rb.name == "Bogie Wheel") sb.Append(rb.name + " com " + rb.worldCenterOfMass.ToString("F2") + " v " + rb.linearVelocity.ToString("F1") + " w " + rb.angularVelocity.ToString("F1") + "\n");
return sb.ToString();
