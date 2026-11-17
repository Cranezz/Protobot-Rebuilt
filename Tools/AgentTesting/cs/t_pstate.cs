var c = Protobot.RobotCode.RobotProgram.Current;
return "program " + (c == null ? "none" : "time " + c.Time.ToString("0.00")) + " sandbox " + Protobot.Motion.Sandbox.Running + " fps " + (1f / Time.smoothDeltaTime).ToString("0");
