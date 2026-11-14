var root = Protobot.Motion.Sandbox.Instance.Root;
UnityEngine.Rigidbody arm = null;
foreach (var rb in root.GetComponentsInChildren<UnityEngine.Rigidbody>()) if (rb.name == "Bogie Rocker") arm = rb;
var frame = arm.GetComponent<UnityEngine.ConfigurableJoint>().connectedBody;
frame.isKinematic = true;
frame.MovePosition(frame.position + UnityEngine.Vector3.up * 4f);
System.AppDomain.CurrentDomain.SetData("rockerStart", null);
System.AppDomain.CurrentDomain.SetData("twist", null);
return "lifted";
