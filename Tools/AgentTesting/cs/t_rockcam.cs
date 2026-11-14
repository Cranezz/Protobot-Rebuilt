var root = Protobot.Motion.Sandbox.Instance.Root;
UnityEngine.Rigidbody arm = null;
foreach (var rb in root.GetComponentsInChildren<UnityEngine.Rigidbody>()) if (rb.name == "Bogie Rocker") arm = rb;
Protobot.Motion.Sandbox.Instance.SetCamera(Protobot.Motion.Sandbox.CameraMode.Free);
Protobot.PivotCamera.Main.SetTransform(arm.worldCenterOfMass, new UnityEngine.Vector3(20f, 30f, 0f), -5f);
return "cam at " + arm.worldCenterOfMass.ToString("F2") + " kin " + arm.isKinematic + " sleeping " + arm.IsSleeping() + " vel " + arm.angularVelocity.magnitude.ToString("F2");
