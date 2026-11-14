var sb = new System.Text.StringBuilder();
foreach (var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) sb.Append(rb.name + " " + rb.mass.ToString("F3") + " kin " + rb.isKinematic + " com " + rb.worldCenterOfMass.ToString("F2") + " v " + rb.linearVelocity.ToString("F2") + "\n");
return sb.ToString();
