Protobot.Devices.V5Brain.Refresh(true);
return "motors in scene " + UnityEngine.Object.FindObjectsByType<Protobot.Motion.MotorDrive>(UnityEngine.FindObjectsSortMode.None).Length + " live " + Protobot.Motion.MotorDrive.Live().Count() + " | " + string.Join(", ", Protobot.Devices.V5Brain.Devices.Select(d => d.kind + ":" + d.port));
