// (the odometry robot built) Saves it with the Odometry example filled for it as its program: Sample Builds/Odometry
// Robot (tracking wheels + IMU).pbb (overwrites: it's ours).
var e = Protobot.RobotCode.Examples.All.First(x => x.name.Contains("dometry"));
var code = Protobot.RobotCode.Examples.Fill(e.code);
Protobot.RobotCode.CodeStore.Set(code);
var data = Protobot.Builds.SceneBuild.ToBuildData(); data.name = "Odometry Robot (tracking wheels + IMU)";
var path = @"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Sample Builds/Odometry Robot (tracking wheels + IMU).pbb";
Protobot.Builds.BuildFile.Write(path, data);
return "saved " + data.parts.Length + " parts, code " + (data.code != null ? data.code.Length : 0) + " chars";
