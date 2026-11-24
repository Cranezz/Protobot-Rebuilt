var m = UnityEngine.Object.FindObjectsByType<Protobot.MovementManager>(FindObjectsSortMode.None).First(x => x.MovingObj != null);
var t = m.MovingObj.transform;
var q = Quaternion.AngleAxis(45f, Vector3.right);
var before = t.forward;
m.NextKind = Protobot.Motion.Linkage.MoveKind.Rotate;
m.DisplaceTo(new Protobot.Transformations.Displacement(t.position, q * t.rotation), 0f);
return "before " + before.ToString("F2") + " after " + t.forward.ToString("F2");
