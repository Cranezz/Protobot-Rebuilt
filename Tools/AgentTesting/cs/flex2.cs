Protobot.Motion.Sandbox.Begin();
var sb = new System.Text.StringBuilder();
foreach (var j in UnityEngine.Object.FindObjectsByType<ConfigurableJoint>(FindObjectsSortMode.None)) sb.Append("joint " + j.angularYMotion + " spring " + j.slerpDrive.positionSpring + "\n");
sb.Append("bodies " + UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Length);
return sb.ToString();
