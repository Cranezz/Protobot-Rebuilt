var data = Protobot.Builds.SceneBuild.ToBuildData(); data.name = "6 Motor Drive with intake";
var path = @"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Sample Builds/6 Motor Drive with intake.pbb";
if (System.IO.File.Exists(path)) return "exists, not overwritten";
var write = typeof(Protobot.Builds.BuildFile).GetMethod("Write");
var ps = write.GetParameters();
write.Invoke(null, ps[0].ParameterType == typeof(string) ? new object[] { path, data } : new object[] { data, path });
return "saved " + data.parts.Length + " parts";
