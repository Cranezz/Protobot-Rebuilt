var sb = new System.Text.StringBuilder();
float maxV = 0f; foreach (var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) maxV = Mathf.Max(maxV, rb.linearVelocity.magnitude);
sb.Append("bodies moving, fastest " + maxV.ToString("F1") + " in/s\n");
Protobot.Motion.Sandbox.TestSticks = null; Protobot.Motion.Sandbox.Stop();
return sb.ToString();
