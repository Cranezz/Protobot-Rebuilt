// (Play) sliding trucks in the build (Motion/TruckSlider). Temp/truckslide.txt: "open" (Sample Builds/Cascade Lift
// (string).pbb, frames for the screws), "moving" (what slides with the right base truck), "slide INCHES" (moves those
// parts along the member as a drag does, then Check: is it still held), "select" (selects that truck: the arrow shows),
// "undo".
var file = System.IO.Path.Combine(Application.dataPath, "../Temp/truckslide.txt");
var cmd = System.IO.File.Exists(file) ? System.IO.File.ReadAllText(file).Trim() : "moving";
var inv = System.Globalization.CultureInfo.InvariantCulture;
var pt = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
if (cmd == "open") {
  var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
  Protobot.PartsManager.DestroyLoadedObjects();
  var data = Protobot.Builds.BuildFile.Read(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Sample Builds/Cascade Lift (string).pbb");
  Protobot.Builds.SceneBuild.GenerateBuild(data);
  for (var i = 0; i < 30; i++) pt.GetMethod("Frames").Invoke(null, new object[] { 10 });
  return "parts " + data.parts.Length;
}
var trucks = UnityEngine.Object.FindObjectsByType<Protobot.Motion.SlideTruck>(FindObjectsSortMode.None).OrderBy(t => t.transform.position.z).ThenByDescending(t => t.transform.position.x).ToList();
var truck = trucks[0];   // the right base truck (stage 2's)
var member = truck.Member;
if (cmd == "undo") { Protobot.StateSystems.StateSystem.instance.Undo(); pt.GetMethod("Frames").Invoke(null, new object[] { 3 }); return "undone; held " + (truck.Member != null); }
if (cmd == "select") {
  var mgr = GameObject.Find("Primary Selection Manager").GetComponent<Protobot.SelectionSystem.SelectionManager>();
  mgr.SetCurrent(new Protobot.SelectionSystem.ObjectSelection { gameObject = truck.gameObject });
  Protobot.Interface.ToolRail.Select("Transform");
  return "selected " + truck.name + " member " + (member != null ? member.name : "-");
}
string problem = null;
var moving = member != null ? Protobot.Motion.TruckSlider.Moving(truck, member, out problem) : null;
if (cmd == "moving") {
  if (member == null) return "not held";
  if (moving == null) return "refused: " + problem;
  var names = moving.GroupBy(o => o.GetComponent<Protobot.SavedObject>().id).Select(g => g.Key + " x" + g.Count());
  return "moving " + moving.Count + " of " + Protobot.PartsManager.FindLoadedObjects().Count + ": " + string.Join(", ", names);
}
if (cmd.StartsWith("slide ")) {
  if (member == null || moving == null) return "not held / refused";
  var d = float.Parse(cmd.Substring(6), inv);
  var axis = member.transform.right;
  var before = moving.Select(o => (Protobot.StateSystems.IElement)new Protobot.StateSystems.ObjectElement(o)).ToList();
  foreach (var o in moving) o.transform.position += axis * d;
  Protobot.StateSystems.StateSystem.AddElements(before);
  Protobot.StateSystems.StateSystem.AddState(new Protobot.StateSystems.State(moving.Select(o => (Protobot.StateSystems.IElement)new Protobot.StateSystems.ObjectElement(o)).ToList()));
  truck.Check();
  var (point, along, low, high) = Protobot.Motion.SlideTruck.Track(member);
  var x = Vector3.Dot(truck.transform.position - point, along);
  System.Collections.Generic.List<GameObject> grp; Protobot.GroupManager.TryGetGroup(truck.gameObject, out grp);
  return "slid " + d + ": x " + x.ToString("F2", inv) + " (track " + low.ToString("F2", inv) + ".." + high.ToString("F2", inv) + ") held " + (truck.Member != null) + " group " + (grp != null ? grp.Count : 0);
}
return "?";
