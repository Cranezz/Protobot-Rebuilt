// (Play) the sandbox's string-stop joints (40" limits): projection from Temp/jproj.txt ("none" / "pos"), and the hinges'.
var mode = System.IO.File.ReadAllText("Temp/jproj.txt").Trim();
var sb = new System.Text.StringBuilder();
foreach (var j in UnityEngine.Object.FindObjectsByType<ConfigurableJoint>(FindObjectsSortMode.None)) {
  var stop = Mathf.Abs(j.linearLimit.limit - 40f) < 0.01f;
  if (stop) j.projectionMode = mode == "none" ? JointProjectionMode.None : JointProjectionMode.PositionAndRotation;
  sb.AppendLine((stop ? "stop " : "joint ") + j.name + " proj " + j.projectionMode + " " + j.projectionDistance + " ang " + j.angularXMotion + " drive max " + j.angularXDrive.maximumForce.ToString("F0") + " damper " + j.angularXDrive.positionDamper.ToString("F1") + " target " + j.targetAngularVelocity.x.ToString("F2") + " body iters " + j.GetComponent<Rigidbody>().solverIterations + " mass " + j.GetComponent<Rigidbody>().mass.ToString("F3") + " to " + (j.connectedBody ? j.connectedBody.mass.ToString("F3") : "world"));
}
return sb.ToString();
