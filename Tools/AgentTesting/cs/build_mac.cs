// Builds the Mac version (Edit mode; needs Unity Hub's "Mac Build Support (Mono)" module for 6000.3.21f1): one .app for
// Intel and Apple Silicon (x64 + ARM64, Mono: IL2CPP for Mac can only be built on a Mac) into
// C:/Dev/ProtobotRebuiltV2/Build/Protobot Mac/Protobot Rebuilt V2.app. The result is in Tools/AgentTesting/build_mac_result.txt.
// Unsigned: macOS asks once (right-click > Open), or `xattr -cr "Protobot Rebuilt V2.app"` after unzipping.
if (!UnityEditor.BuildPipeline.IsBuildTargetSupported(UnityEditor.BuildTargetGroup.Standalone, UnityEditor.BuildTarget.StandaloneOSX))
    return "Mac Build Support isn't installed (Unity Hub > Installs > 6000.3.21f1 > Add modules > Mac Build Support (Mono))";
// The release's Mac build keeps the Windows build's number (PlayerPrefs "t_mac_keep" = 1): multiplayer joins only the same build.
if (UnityEngine.PlayerPrefs.GetInt("t_mac_keep", 0) == 1) { UnityEditor.EditorPrefs.SetBool("V2.Build.KeepNumber", true); UnityEngine.PlayerPrefs.DeleteKey("t_mac_keep"); }
var scenes = new System.Collections.Generic.List<string>();
foreach (var s in UnityEditor.EditorBuildSettings.scenes) if (s.enabled) scenes.Add(s.path);
// Both kinds of Mac in one app (the setting lives in the Mac module's own assembly: set by reflection).
var settingsType = System.Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor.OSXStandalone.Extensions");
var archType = System.Type.GetType("UnityEditor.Build.OSArchitecture, UnityEditor.CoreModule");
var archNote = "architecture unchanged";
try {
    var prop = settingsType?.GetProperty("architecture", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
    if (prop != null) { prop.SetValue(null, System.Enum.Parse(prop.PropertyType, "x64ARM64")); archNote = "x64 + ARM64"; }
} catch (System.Exception e) { archNote = "architecture: " + e.Message; }
// Mono for the Mac (the Standalone setting is shared with Windows: put back after).
var oldBackend = UnityEditor.PlayerSettings.GetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone);
UnityEditor.PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, UnityEditor.ScriptingImplementation.Mono2x);
var options = new UnityEditor.BuildPlayerOptions {
  scenes = scenes.ToArray(),
  locationPathName = @"C:/Dev/ProtobotRebuiltV2/Build/Protobot Mac/Protobot Rebuilt V2.app",
  target = UnityEditor.BuildTarget.StandaloneOSX,
  targetGroup = UnityEditor.BuildTargetGroup.Standalone,
  options = UnityEditor.BuildOptions.None
};
// Unity services packages each carry an Apple privacy manifest (PrivacyInfo.xcprivacy, for iOS): a Mac build tries to
// copy them all to one place and fails ("Plugins colliding"). A Mac app doesn't need them: left out of this build.
var privacy = new System.Collections.Generic.List<UnityEditor.PluginImporter>();
foreach (var imp in UnityEditor.PluginImporter.GetAllImporters())
    if (imp != null && imp.assetPath.EndsWith("PrivacyInfo.xcprivacy")) { imp.SetIncludeInBuildDelegate(path => false); privacy.Add(imp); }
var hadPref = UnityEditor.EditorPrefs.HasKey("Never ask about models check");
UnityEditor.EditorPrefs.SetBool("Never ask about models check", true);
UnityEditor.Build.Reporting.BuildReport report;
try { report = UnityEditor.BuildPipeline.BuildPlayer(options); }
finally {
    if (!hadPref) UnityEditor.EditorPrefs.DeleteKey("Never ask about models check");
    UnityEditor.PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, oldBackend);
    foreach (var imp in privacy) imp.SetIncludeInBuildDelegate(path => true);
}
var sum = report.summary;
var errors = new System.Text.StringBuilder();
foreach (var step in report.steps) foreach (var m in step.messages) if (m.type == UnityEngine.LogType.Error) errors.AppendLine(m.content);
System.IO.File.WriteAllText(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/build_mac_result.txt", sum.result + " " + sum.totalSize + " bytes, " + sum.totalTime + ", errors " + sum.totalErrors + ", " + archNote + "\n" + errors);
return sum.result + " " + sum.totalErrors + " " + archNote + " (privacy files left out: " + privacy.Count + ")";
