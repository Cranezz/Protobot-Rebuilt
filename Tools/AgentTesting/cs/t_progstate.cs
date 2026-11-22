var p = Protobot.RobotCode.RobotProgram.Current;
return p == null ? "no program" : ("state " + p.State + " t=" + p.Time.ToString("0.0") + " fps " + (1f / UnityEngine.Time.smoothDeltaTime).ToString("0"));
