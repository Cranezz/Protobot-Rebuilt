foreach (var d in Protobot.Devices.V5Brain.Devices) { var s = d as Protobot.Devices.SolenoidDevice; if (s != null && s.cylinders.Count > 0) s.Set(true); }
Protobot.Motion.Sandbox.TestSticks = () => (new UnityEngine.Vector2(0f, 0.6f), new UnityEngine.Vector2(0.5f, 0f));
return "set";
