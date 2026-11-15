foreach (var m in UnityEngine.Object.FindObjectsByType<Protobot.Motion.MotorDrive>(UnityEngine.FindObjectsSortMode.None)) m.Manual = 0f;
return "arm stopped";
