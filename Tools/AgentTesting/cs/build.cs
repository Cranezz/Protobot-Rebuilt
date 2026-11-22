var scenes = new System.Collections.Generic.List<string>();
foreach (var s in UnityEditor.EditorBuildSettings.scenes) if (s.enabled) scenes.Add(s.path);
var options = new UnityEditor.BuildPlayerOptions {
  scenes = scenes.ToArray(),
  locationPathName = @"C:/Dev/ProtobotRebuiltV2/Build/Protobot Beta 1.9/ProtobotRebuiltV2.exe",
  target = UnityEditor.BuildTarget.StandaloneWindows64,
  options = UnityEditor.BuildOptions.None
};
// Easy Performant Outline asks "check models?" before every build (a modal dialog): answer Skip without the window.
var hadPref = UnityEditor.EditorPrefs.HasKey("Never ask about models check");
UnityEditor.EditorPrefs.SetBool("Never ask about models check", true);
UnityEditor.Build.Reporting.BuildReport report;
try { report = UnityEditor.BuildPipeline.BuildPlayer(options); }
finally { if (!hadPref) UnityEditor.EditorPrefs.DeleteKey("Never ask about models check"); }
var sum = report.summary;
System.IO.File.WriteAllText(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/build_result.txt", sum.result + " " + sum.totalSize + " bytes, " + sum.totalTime + ", errors " + sum.totalErrors);
return sum.result + " " + sum.totalErrors;
