Protobot.Motion.Sandbox.TestSticks = () => (Vector2.zero, Vector2.zero);
foreach (var m in UnityEngine.Object.FindObjectsByType<Protobot.Motion.MotorDrive>(UnityEngine.FindObjectsSortMode.None)) m.Manual = 0f;
return "stopped";
