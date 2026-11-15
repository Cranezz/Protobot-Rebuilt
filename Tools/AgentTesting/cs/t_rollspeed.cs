var sb = new System.Text.StringBuilder();
foreach (var rb in Protobot.Motion.Sandbox.Instance.Root.GetComponentsInChildren<Rigidbody>())
    if (rb.gameObject.layer == Protobot.Motion.Sandbox.Layer && rb.mass > 0.1f && rb.mass < 1f) sb.Append("body " + rb.mass.ToString("F2") + " w " + rb.angularVelocity.ToString("F1") + "; ");
return sb.ToString();
