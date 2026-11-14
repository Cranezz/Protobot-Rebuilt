
UnityEngine.Transform motor = null, screw = null;
foreach (var so in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None)) { if (so.id.StartsWith("MOTR") && motor == null) motor = so.transform; if (so.id == "SCRW-2.50in" && screw == null) screw = so.transform; }
var cam = Protobot.PivotCamera.Main;
var b = motor.GetComponentInChildren<UnityEngine.Renderer>().bounds;
if (screw != null) b.Encapsulate(screw.GetComponentInChildren<UnityEngine.Renderer>().bounds);
cam.SetTransform(b.center, new UnityEngine.Vector3(30f, 200f, 0f), -b.size.magnitude * 0.9f);
return "motor at " + b.center;
