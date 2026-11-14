var o = GameObject.Find("ELBOW"); var d = o.GetComponent<Protobot.Screw>().holeDetector;
var cyl = GameObject.Find("50mmPiston(Clone)");
Protobot.HoleCollider port = null; foreach (var h in cyl.GetComponentsInChildren<Protobot.HoleCollider>()) if (h.holeType == Protobot.HoleCollider.HoleType.Threaded && h.transform.localPosition.x > 1f) port = h;
var along = (bool)typeof(Protobot.HoleDetector).GetMethod("Along", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(d, new object[] { port });
var rb = d.GetComponent<Rigidbody>();
return "along " + along + " intersect " + d.IsHoleIntersecting(port) + " rb " + (rb != null) + " kin " + (rb != null && rb.isKinematic) + " detLayer " + d.gameObject.layer + " portLayer " + port.gameObject.layer + " portColl " + port.GetComponent<Collider>().GetType().Name + " trig " + port.GetComponent<Collider>().isTrigger + " active " + d.gameObject.activeInHierarchy + " enabled " + d.enabled;
