foreach (var j in UnityEngine.Object.FindObjectsByType<ConfigurableJoint>(FindObjectsSortMode.None)) j.angularXMotion = ConfigurableJointMotion.Locked;
foreach (var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
return "locked";
