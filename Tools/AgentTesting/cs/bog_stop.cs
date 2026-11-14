foreach (var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) if (rb.name == "Body" || rb.name == "Bogie Wheel") { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
return "stopped";
