var root = Protobot.Motion.Sandbox.Instance.Root;
UnityEngine.Rigidbody arm = null;
foreach (var rb in root.GetComponentsInChildren<UnityEngine.Rigidbody>()) if (rb.name == "Bogie Rocker") arm = rb;
var j = arm.GetComponent<UnityEngine.ConfigurableJoint>();
var cf = arm.GetComponent<UnityEngine.ConstantForce>(); if (cf == null) cf = arm.gameObject.AddComponent<UnityEngine.ConstantForce>();
var sign = cf.relativeTorque.sqrMagnitude > 0 && UnityEngine.Vector3.Dot(cf.relativeTorque, j.axis) > 0 ? -1f : 1f;
cf.relativeTorque = j.axis * 30f * sign;
return "steady torque " + sign + " | vel " + arm.angularVelocity.magnitude.ToString("F2") + " constraints " + arm.constraints;
