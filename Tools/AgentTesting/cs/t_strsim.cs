var sb = new System.Text.StringBuilder();
var list = Protobot.Motion.Sandbox.Strings();
sb.AppendLine("sim strings " + list.Count + " of " + Protobot.Motion.StringLine.All.Count);
foreach (var s in list) sb.AppendLine(" " + s.line.GetInstanceID() + " slack " + s.slack + " moving " + s.moving + " broken " + s.broken);
foreach (var j in UnityEngine.Object.FindObjectsByType<ConfigurableJoint>(FindObjectsSortMode.None)) if (j.xMotion == ConfigurableJointMotion.Limited && j.linearLimit.limit > 39f) sb.AppendLine(" joint limit " + j.linearLimit.limit + " on " + j.GetComponent<Rigidbody>().GetInstanceID());
return sb.ToString();
