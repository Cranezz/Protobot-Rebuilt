// (Play) Tutorial 2's target: Temp/tdrive.txt "build" = a new build with the tutorial drivetrain (MotionTestRig.TutorialDrive)
// at the origin, its stage tags handed to DemoPlayer.Target; "finish" = motors set (FinishSixMotorDrive); "save PATH".
var cmd = System.IO.File.Exists("Temp/tdrive.txt") ? System.IO.File.ReadAllText("Temp/tdrive.txt").Trim() : "build";
if (cmd == "build") {
    var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
    Protobot.PartsManager.DestroyLoadedObjects();
    Protobot.Builds.DemoPlayer.Target.Clear();
    var parts = Protobot.EditorTools.MotionTestRig.TutorialDrive(Vector3.zero);
    foreach (var kv in Protobot.EditorTools.MotionTestRig.Tags) Protobot.Builds.DemoPlayer.Target[kv.Key] = kv.Value;
    var counts = Protobot.EditorTools.MotionTestRig.Tags.GroupBy(kv => kv.Value).Select(g => g.Key + " " + g.Count());
    return "parts " + parts.Count + ": " + string.Join(", ", counts);
}
if (cmd == "build5") {   // tutorial 5: the whole drivetrain (tag "drive") plus the corner pieces' spacers and screws
    var close = GameObject.Find("Close Button"); if (close != null) close.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
    Protobot.PartsManager.DestroyLoadedObjects();
    Protobot.Builds.DemoPlayer.Target.Clear();
    var parts = Protobot.EditorTools.MotionTestRig.TutorialDrive(Vector3.zero);
    foreach (var k in Protobot.EditorTools.MotionTestRig.Tags.Keys.ToList()) Protobot.EditorTools.MotionTestRig.Tags[k] = "drive";
    var corners = Protobot.EditorTools.MotionTestRig.TutorialCorners(3.249f + 0.001f + 0.0625f);
    foreach (var kv in Protobot.EditorTools.MotionTestRig.Tags) Protobot.Builds.DemoPlayer.Target[kv.Key] = kv.Value;
    var counts = Protobot.EditorTools.MotionTestRig.Tags.GroupBy(kv => kv.Value).Select(g => g.Key + " " + g.Count());
    return "parts " + (parts.Count + corners.Count) + ": " + string.Join(", ", counts);
}
if (cmd == "finish") {
    var parts = UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None).Select(s => s.gameObject).ToList();
    Protobot.EditorTools.MotionTestRig.FinishSixMotorDrive(parts);
    var train = Protobot.Motion.GearTrain.Rotors();
    return "finished; rotors " + train.Count;
}
if (cmd.StartsWith("save ")) {
    var path = cmd.Substring(5).Trim();
    var data = Protobot.Builds.SceneBuild.ToBuildData(); data.name = System.IO.Path.GetFileNameWithoutExtension(path);
    Protobot.Builds.BuildFile.Write(path, data);
    var rows = Protobot.EditorTools.MotionTestRig.Tags.Where(kv => kv.Key != null).Select(kv => { var so = kv.Key.GetComponent<Protobot.SavedObject>(); var p = kv.Key.transform.position; return kv.Value + " " + p.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " " + p.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " " + p.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " " + so.id; });
    System.IO.File.WriteAllLines(path + ".tags.txt", rows);
    return "saved " + data.parts.Length + " parts, tags " + rows.Count();
}
return "?";
