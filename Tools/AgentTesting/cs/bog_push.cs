foreach (var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) if (rb.name == "Body" || rb.name == "Bogie Wheel") rb.linearVelocity = new Vector3(20f, rb.linearVelocity.y, 0f);
return "pushed";
