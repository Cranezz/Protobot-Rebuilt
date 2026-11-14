var sb = new System.Text.StringBuilder();
foreach (var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) {
  sb.Append(rb.name + " rot " + rb.rotation.eulerAngles.ToString("F1") + " inertia " + rb.inertiaTensor.ToString("F5") + " com local " + rb.centerOfMass.ToString("F2") + " damp " + rb.linearDamping + "/" + rb.angularDamping + "\n");
  foreach (var j in rb.GetComponents<ConfigurableJoint>()) sb.Append("  joint anchor " + j.anchor.ToString("F2") + " conn " + j.connectedAnchor.ToString("F2") + " axis " + j.axis + " world " + rb.transform.TransformPoint(j.anchor).ToString("F2") + " connWorld " + j.connectedBody.transform.TransformPoint(j.connectedAnchor).ToString("F2") + "\n");
  foreach (var c in rb.GetComponentsInChildren<Collider>()) sb.Append("  collider " + c.name + " bounds " + c.bounds.center.ToString("F2") + " " + c.bounds.size.ToString("F2") + " mat " + (c.sharedMaterial ? c.sharedMaterial.dynamicFriction.ToString() : "-") + "\n");
}
return sb.ToString();
