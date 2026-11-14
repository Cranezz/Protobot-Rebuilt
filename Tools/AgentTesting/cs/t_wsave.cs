// "save": writes the scene to Temp/wires.pbb; "load": reads it back; reports the wires.
var arg = System.IO.File.ReadAllText("Temp/wsave.txt").Trim();
var path = @"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Temp/wires.pbb";
if (arg == "save") {
  var data = Protobot.Builds.SceneBuild.ToBuildData(); data.name = "wires";
  var write = typeof(Protobot.Builds.BuildFile).GetMethod("Write");
  var ps = write.GetParameters();
  write.Invoke(null, ps[0].ParameterType == typeof(string) ? new object[] { path, data } : new object[] { data, path });
}
if (arg == "load") {
  Protobot.PartsManager.DestroyLoadedObjects();
  var data = Protobot.Builds.BuildFile.Read(path);
  Protobot.Builds.SceneBuild.GenerateBuild(data);
}
var sb = new System.Text.StringBuilder(arg + ": ");
foreach (var w in Protobot.Motion.Wire.Wires) if (w.gameObject.layer != 12) sb.Append(w.Title + " " + w.GetComponent<Protobot.SavedObject>().id + " nodes " + w.nodes.Count + " | " + w.Describe(w.Length).Replace("\n", " / ") + "; ");
foreach (var m in UnityEngine.Object.FindObjectsByType<Protobot.Motion.MotorDrive>(FindObjectsSortMode.None)) sb.Append(" motor " + m.Label);
return sb.ToString();
