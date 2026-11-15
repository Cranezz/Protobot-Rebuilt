Rigidbody chassis = null; foreach (var rb in Protobot.Motion.Sandbox.Instance.Root.GetComponentsInChildren<Rigidbody>()) if (rb.gameObject.layer == Protobot.Motion.Sandbox.Layer && (chassis == null || rb.mass > chassis.mass)) chassis = rb;
Protobot.Motion.Sandbox.Instance.Follow = false;
var yaw = Quaternion.LookRotation(Vector3.ProjectOnPlane(chassis.transform.forward, Vector3.up)).eulerAngles.y;
Protobot.PivotCamera.Main.SetTransform(chassis.position, new Vector3(30f, yaw + 120f, 0f), -34f);
return "camera on the robot";
