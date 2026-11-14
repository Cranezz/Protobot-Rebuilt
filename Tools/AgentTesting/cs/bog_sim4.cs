var sb = new System.Text.StringBuilder();
foreach (var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
  sb.Append(rb.name + " com " + rb.worldCenterOfMass.ToString("F2") + " v " + rb.linearVelocity.ToString("F2") + " w " + rb.angularVelocity.ToString("F2") + "\n");
return sb.ToString();
