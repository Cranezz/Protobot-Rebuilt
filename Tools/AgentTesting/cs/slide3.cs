var member = GameObject.Find("Outer Slide Member(Clone)");
var truck = GameObject.Find("Inner Acetal Slide Truck(Clone)");
// the slide stands up at 60°, grounded
member.transform.RotateAround(member.transform.position, Vector3.forward, 60f);
truck.transform.RotateAround(member.transform.position, Vector3.forward, 60f);
Protobot.Motion.Linkage.SetGrounded(member, true);
Protobot.Motion.Sandbox.Begin();
var n = 0; foreach (var j in UnityEngine.Object.FindObjectsByType<ConfigurableJoint>(FindObjectsSortMode.None)) n++;
return "joints " + n;
