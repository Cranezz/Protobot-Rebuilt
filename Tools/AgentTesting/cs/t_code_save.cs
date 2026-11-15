var code = "from vex import *\n# saved \"quotes\" and unicode é\nprint('hi')\n";
Protobot.RobotCode.CodeStore.Set(code);
var data = Protobot.Builds.SceneBuild.ToBuildData();
var path = System.IO.Path.Combine(UnityEngine.Application.temporaryCachePath, "code_roundtrip.pbb");
Protobot.Builds.BuildFile.Write(path, data);
var back = Protobot.Builds.BuildFile.Read(path);
Protobot.RobotCode.CodeStore.Load(back.code);
return "same=" + (Protobot.RobotCode.CodeStore.Source == code) + " compare=" + data.CompareData(back);
