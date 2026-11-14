var scenes = new System.Collections.Generic.List<string>();
foreach (var s in UnityEditor.EditorBuildSettings.scenes) if (s.enabled) scenes.Add(s.path);
var options = new UnityEditor.BuildPlayerOptions {
  scenes = scenes.ToArray(),
  locationPathName = @"C:/Dev/ProtobotRebuiltV2/Build/Protobot Beta 1.9/ProtobotRebuiltV2.exe",
  target = UnityEditor.BuildTarget.StandaloneWindows64,
  options = UnityEditor.BuildOptions.None
};
var report = UnityEditor.BuildPipeline.BuildPlayer(options);
var sum = report.summary;
System.IO.File.WriteAllText(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/build_result.txt", sum.result + " " + sum.totalSize + " bytes, " + sum.totalTime + ", errors " + sum.totalErrors);
return sum.result + " " + sum.totalErrors;
