// Bolts a V5 Inertial Sensor onto the robot's highest upward hole (test only: the build file isn't saved).
var sb = new System.Text.StringBuilder();
Protobot.HoleCollider top = null;
foreach (var h in UnityEngine.Object.FindObjectsByType<Protobot.HoleCollider>(UnityEngine.FindObjectsSortMode.None)) {
    if (Mathf.Abs(h.transform.forward.y) < 0.98f) continue;
    var part = h.GetComponentInParent<Protobot.SavedObject>();
    if (part == null || !part.name.Contains("Aluminum")) continue;
    if (top == null || h.transform.position.y > top.transform.position.y + 0.01f || (Mathf.Abs(h.transform.position.y - top.transform.position.y) < 0.01f && Mathf.Abs(h.transform.position.x) + Mathf.Abs(h.transform.position.z) < Mathf.Abs(top.transform.position.x) + Mathf.Abs(top.transform.position.z))) top = h;
}
if (top == null) return "no hole";
var holeTop = top.transform.position + Vector3.up * top.transform.lossyScale.z / 2f;
sb.AppendLine("top hole " + top.transform.position + " on " + top.GetComponentInParent<Protobot.SavedObject>().name);
var imu = Protobot.PartsManager.GeneratePart("SNSR-Inertial-V5", holeTop + Vector3.up * 3f, Quaternion.identity);
UnityEngine.Physics.SyncTransforms();
Protobot.HoleCollider low = null;
foreach (var h in imu.GetComponentsInChildren<Protobot.HoleCollider>(true)) {
    sb.AppendLine(" imu hole " + h.transform.position + " fwd " + h.transform.forward + " size " + h.transform.lossyScale);
    if (Mathf.Abs(h.transform.forward.y) > 0.98f && (low == null || h.transform.position.y < low.transform.position.y)) low = h;
}
if (low == null) return sb + "no vertical imu hole";
var lowBottom = low.transform.position - Vector3.up * low.transform.lossyScale.z / 2f;
imu.transform.position += holeTop - lowBottom;
UnityEngine.Physics.SyncTransforms();
var ok = Protobot.EditorTools.MotionTestRig.Bolt(low.transform.position + Vector3.up * 0.2f, Vector3.down);
sb.AppendLine("bolted " + ok);
return sb.ToString();
