var cam = Protobot.PivotCamera.Main;
Rigidbody main = null; float m = 0; foreach (var b in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) if (b.gameObject.layer == 16 && b.mass > m) { m = b.mass; main = b; }
return "focus " + cam.focusPosition.ToString("F1") + " robot " + main.worldCenterOfMass.ToString("F1") + " gap " + (cam.focusPosition - main.worldCenterOfMass).magnitude.ToString("F1");
