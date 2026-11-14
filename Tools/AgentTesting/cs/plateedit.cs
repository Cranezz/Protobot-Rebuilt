foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None)) if (s.id.Contains("License Plate")) { Protobot.Motion.LicensePlate.Edit(s.gameObject); return "opened"; }
return "none";
