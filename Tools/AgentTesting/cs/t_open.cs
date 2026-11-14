var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>()?.onClick.Invoke();
var manager = UnityEngine.Object.FindFirstObjectByType<Protobot.Builds.BuildsManager>();
var sb = new System.Text.StringBuilder();
foreach (var path in new[] { @"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/builds/V2 Features.pbb", @"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Tools/AgentTesting/builds/6 Motor Drive with intake.pbb" }) {
  if (!System.IO.File.Exists(path)) { sb.Append("missing " + path + "\n"); continue; }
  var data = Protobot.Builds.BuildsManager.ParsePath(path);
  manager.AttemptLoad(data, path);
  // an unsaved-changes prompt would stop it: say yes to loading without saving
  if (manager.buildPath != path) manager.LoadAttempt();
  sb.Append(System.IO.Path.GetFileName(path) + ": loaded " + Protobot.PartsManager.FindLoadedObjects().Count + " parts, path set " + (manager.buildPath == path) + "\n");
}
return sb.ToString();
