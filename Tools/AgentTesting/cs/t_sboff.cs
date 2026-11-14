var t = Protobot.Motion.PneumaticTube.All[0]; var r = "tube length in sim " + t.Length.ToString("F2"); Protobot.Motion.Sandbox.Stop(); return r + " stopped " + !Protobot.Motion.Sandbox.Running;
