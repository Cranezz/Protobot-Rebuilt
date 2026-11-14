var pl = Protobot.Placement.Instance;
Protobot.Screw sc = null; foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.Screw>(FindObjectsSortMode.None)) if (s.GetComponent<Protobot.SavedObject>().id == "SCRW-1.75in") sc = s;
var head = sc.transform.position; var down = -sc.transform.forward;
var p = pl.transform.position; var fwd = pl.transform.forward;
return "ghost face at along " + Vector3.Dot(p - head, down).ToString("F3") + " off-axis " + Vector3.ProjectOnPlane(p - head, down).magnitude.ToString("F3") + " ghost forward " + fwd.ToString("F2") + " (screw down " + down.ToString("F2") + ") mouse " + Protobot.MouseInput.Position;
