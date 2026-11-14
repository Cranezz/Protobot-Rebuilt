var bodies = UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
Vector3 sum = Vector3.zero; float mass = 0f; foreach (var b in bodies) if (!b.isKinematic) { sum += b.worldCenterOfMass * b.mass; mass += b.mass; }
return "frame " + Time.frameCount + " bodies " + bodies.Length + " com " + (mass > 0 ? (sum / mass).ToString("F1") : "-") + " fps " + (1f / Time.smoothDeltaTime).ToString("F0");
