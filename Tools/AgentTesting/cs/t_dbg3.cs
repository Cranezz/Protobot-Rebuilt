var o = GameObject.Find("ELBOW"); var d = o.GetComponent<Protobot.Screw>().holeDetector;
var cyl = GameObject.Find("50mmPiston(Clone)");
var s = "";
foreach (var h in cyl.GetComponentsInChildren<Protobot.HoleCollider>()) s += h.holeType + " det " + h.detectors.Count + " pos " + h.transform.position.ToString("F2") + "; ";
d.AddHole(cyl.GetComponentsInChildren<Protobot.HoleCollider>()[3]);
return s + " | after add: holes " + d.holes.Count + " found " + d.TargetHoleFound;
