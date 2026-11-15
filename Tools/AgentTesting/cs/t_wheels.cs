var sb = new System.Text.StringBuilder("time " + Time.time.ToString("F1") + " fixed " + Time.fixedTime.ToString("F1") + "; ");
foreach (var rb in Protobot.Motion.Sandbox.Instance.Root.GetComponentsInChildren<Rigidbody>())
    if (rb.gameObject.layer == Protobot.Motion.Sandbox.Layer && rb.mass < 0.5f) sb.Append(rb.angularVelocity.magnitude.ToString("F1") + " ");
return sb.ToString();
