// Prints the drive Insert devices measures (wheel size, track width, gear ratio) for the loaded build.
Protobot.Motion.Sandbox.Stop();
var cfg = Protobot.RobotCode.DeviceConfig.Generate();
var d = Protobot.RobotCode.DeviceConfig.LastDrive;
return d.HasValue ? "wheel " + d.Value.diameter + " track " + d.Value.track + " ratio " + d.Value.ratio + " rpm " + d.Value.motorRpm : "no drive\n" + cfg;
