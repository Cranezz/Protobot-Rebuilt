// (Play mode, after t_vis1.cs) what Insert devices writes for the Vision Sensor: Python, VEXcode C++, PROS.
return Protobot.RobotCode.DeviceConfig.Generate() + "\n----\n" + Protobot.RobotCode.DeviceConfig.GenerateCpp(false) + "\n----\n" + Protobot.RobotCode.DeviceConfig.GenerateCpp(true);
