var sb = new System.Text.StringBuilder();
foreach (var m in Protobot.Motion.MotorDrive.Live()) sb.AppendLine(m.Label + " manual " + m.Manual + " cmd " + m.Command + " level " + m.Level + " drives " + Protobot.Motion.Sandbox.Instance.Drives(m));
return sb.ToString();
