var sb = new System.Text.StringBuilder();
GameObject motor = null; foreach (var m in UnityEngine.Object.FindObjectsByType<Protobot.Motion.MotorDrive>(FindObjectsSortMode.None)) { motor = m.gameObject; break; }
var parts = Protobot.ConnectedObjExtensions.GetConnectedObjects(motor, true, true);
sb.Append("group of " + parts.Count + "\n");
Protobot.Builds.SavedGroups.Save("Agent test group", motor, parts);
var before = Protobot.PartsManager.FindLoadedObjects().Count;
Protobot.Builds.SavedGroups.Place("Agent test group");
sb.Append("parts " + before + " -> " + Protobot.PartsManager.FindLoadedObjects().Count + "\n");
sb.Append("catalog has saved groups " + System.Linq.Enumerable.Any(Protobot.Interface.PartsCatalog.Nodes, n => n.name == "Saved groups"));
Protobot.Builds.SavedGroups.Delete("Agent test group");
return sb.ToString();
