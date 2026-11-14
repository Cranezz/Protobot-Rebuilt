var pc = UnityEngine.Object.FindFirstObjectByType<Protobot.Motion.PneumaticCylinder>();
pc.Loose = true;
Protobot.Motion.Sandbox.Begin();
var sb = new System.Text.StringBuilder();
sb.Append("loose in sim " + Protobot.Motion.Sandbox.Instance.IsLoose(pc) + " slides " + Protobot.Motion.Sandbox.Instance.Slides(pc) + "\n");
foreach (var j in UnityEngine.Object.FindObjectsByType<ConfigurableJoint>(FindObjectsSortMode.None)) if (j.xMotion == ConfigurableJointMotion.Limited) sb.Append("slide drive max " + j.xDrive.maximumForce.ToString("F1") + "\n");
return sb.ToString();
