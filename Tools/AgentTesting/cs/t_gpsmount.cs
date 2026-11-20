// Bolts a GPS Sensor to the robot's frontmost (or, with PlayerPrefs "t_gps_back" = "1", rearmost) metal hole whose
// axis runs front-back, its camera facing out that way (test only: the build file isn't saved).
var back = UnityEngine.PlayerPrefs.GetString("t_gps_back") == "1";
var way = back ? Vector3.back : Vector3.forward;
var sb = new System.Text.StringBuilder();
foreach (var old in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None).Where(s => Protobot.Devices.V5Brain.KindOf(s.gameObject) == Protobot.Devices.DeviceKind.Gps).ToList())
    UnityEngine.Object.Destroy(old.gameObject);
Protobot.HoleCollider best = null;
foreach (var h in UnityEngine.Object.FindObjectsByType<Protobot.HoleCollider>(UnityEngine.FindObjectsSortMode.None)) {
    if (Mathf.Abs(h.transform.forward.z) < 0.98f) continue;
    var part = h.GetComponentInParent<Protobot.SavedObject>();
    if (part == null || !(part.name.Contains("Aluminum") || part.name.Contains("Channel") || part.name.Contains("Plate") || part.name.Contains("Steel"))) continue;
    var score = Vector3.Dot(h.transform.position, way) * 10f + h.transform.position.y - Mathf.Abs(h.transform.position.x) * 0.5f;
    if (best == null || score > Vector3.Dot(best.transform.position, way) * 10f + best.transform.position.y - Mathf.Abs(best.transform.position.x) * 0.5f) best = h;
}
if (best == null) return "no front-back hole";
var face = best.transform.position + way * best.transform.lossyScale.z / 2f;
sb.AppendLine("hole " + best.transform.position.ToString("F2") + " on " + best.GetComponentInParent<Protobot.SavedObject>().name);
var gps = Protobot.PartsManager.GeneratePart("VXSENSORS-GPS Sensor", face + way * 3f, Quaternion.identity);
var lk = Protobot.Devices.SensorFrame.Look(gps);
gps.transform.rotation = Quaternion.FromToRotation(gps.transform.TransformDirection(lk.dir), way) * gps.transform.rotation;
UnityEngine.Physics.SyncTransforms();
Protobot.HoleCollider hole = null;
foreach (var h in gps.GetComponentsInChildren<Protobot.HoleCollider>(true))
    if (Mathf.Abs(Vector3.Dot(h.transform.forward, way)) > 0.98f && (hole == null || Vector3.Dot(h.transform.position, way) < Vector3.Dot(hole.transform.position, way) - 0.01f || Mathf.Abs(h.transform.position.y - face.y) < Mathf.Abs(hole.transform.position.y - face.y) - 0.01f)) hole = h;
if (hole == null) return sb + "no gps hole along its look";
var holeBack = hole.transform.position - way * hole.transform.lossyScale.z / 2f;
gps.transform.position += face - holeBack;
UnityEngine.Physics.SyncTransforms();
var ok = Protobot.EditorTools.MotionTestRig.Bolt(hole.transform.position + way * 0.2f, -way);
sb.AppendLine("bolted " + ok + ", gps lens " + (gps.transform.TransformPoint(lk.origin)).ToString("F1"));
return sb.ToString();
