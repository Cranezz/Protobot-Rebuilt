var motors = Protobot.Motion.MotorDrive.Live();
motors[0].SetCartridge(100, false);
motors[1].SetCartridge(200, false);
return "set";
