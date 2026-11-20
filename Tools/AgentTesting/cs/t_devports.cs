Protobot.Devices.V5Brain.Refresh(true);
return string.Join("\n", Protobot.Devices.V5Brain.Devices.Select(d => (d.threeWire ? "3W " : "") + d.port + " " + d.kind + " " + Protobot.Motion.Controls.Side(d.part) + " x=" + d.part.transform.position.x.ToString("0.0") + " z=" + d.part.transform.position.z.ToString("0.0")));
