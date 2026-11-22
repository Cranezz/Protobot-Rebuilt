// (Play) Trailer footage. Temp/shot.txt, one command:
//   load PATH                         open a build (closes the home menu)
//   orbit NAME SECONDS RADIUS HEIGHT START SPEED LOOK ZOOM HIDE   record an orbit round the build's middle (HIDE 1 = no interface)
//   still NAME SECONDS HIDE          record what's on screen as it is
//   follow NAME SECONDS RADIUS HEIGHT START SPEED LOOK HIDE      orbit round the simulated robot (needs a running sim)
//   status                            frames recorded so far (-1 = not recording)
// Frames go to C:/Dev/ProtobotRebuiltV2/Video/shots/NAME/.
var cmd = System.IO.File.ReadAllText("Temp/shot.txt").Trim().Split(' ');
var root = "C:/Dev/ProtobotRebuiltV2/Video/shots/";
System.Func<int, float> F = i => float.Parse(cmd[i], System.Globalization.CultureInfo.InvariantCulture);
if (cmd[0] == "status") return "frame " + Protobot.Builds.FrameRecorder.Frame;
if (cmd[0] == "load") {
    var launch = UnityEngine.GameObject.Find("Launch Canvas");
    if (launch != null) foreach (var b in launch.GetComponentsInChildren<UnityEngine.UI.Button>(true)) if (b.name == "Close Button" && b.transform.parent.name == "Splash Screen" && b.gameObject.activeInHierarchy) { b.onClick.Invoke(); break; }
    Protobot.PartsManager.DestroyLoadedObjects();
    var path = string.Join(" ", cmd, 1, cmd.Length - 1);
    var data = Protobot.Builds.BuildFile.Read(path);
    Protobot.Builds.SceneBuild.GenerateBuild(data);
    var manager = UnityEngine.Object.FindAnyObjectByType<Protobot.Builds.BuildsManager>();
    manager.buildPath = path;
    return "loaded " + data.parts.Length;
}
System.Func<UnityEngine.Bounds> BuildBounds = () => {
    var bounds = new UnityEngine.Bounds(); var first = true;
    foreach (var s in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None))
        foreach (var r in s.GetComponentsInChildren<UnityEngine.Renderer>()) {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
        }
    return bounds;
};
if (cmd[0] == "orbit") {
    var b = BuildBounds();
    Protobot.Builds.FrameRecorder.Record(root + cmd[1], F(2));
    if (cmd[9] == "1") Protobot.Builds.FrameRecorder.HideInterface(true);
    Protobot.Builds.FrameRecorder.Orbit(b.center, null, F(3), F(4), F(5), F(6), F(7), F(8));
    return "recording " + cmd[1] + " round " + b.center + " size " + b.size;
}
if (cmd[0] == "still") {
    Protobot.Builds.FrameRecorder.Record(root + cmd[1], F(2));
    if (cmd[3] == "1") Protobot.Builds.FrameRecorder.HideInterface(true);
    return "recording " + cmd[1];
}
if (cmd[0] == "follow") {
    UnityEngine.Transform target = null; float best = 0f;
    foreach (var rb in UnityEngine.Object.FindObjectsByType<UnityEngine.Rigidbody>(UnityEngine.FindObjectsSortMode.None))
        if (rb.gameObject.layer == 16 && rb.mass > best) { best = rb.mass; target = rb.transform; }
    if (target == null) return "no simulated robot";
    Protobot.Builds.FrameRecorder.Record(root + cmd[1], F(2));
    if (cmd[8] == "1") Protobot.Builds.FrameRecorder.HideInterface(true);
    Protobot.Builds.FrameRecorder.Orbit(UnityEngine.Vector3.zero, target, F(3), F(4), F(5), F(6), F(7));
    return "recording " + cmd[1] + " following " + target.name;
}
return "?";
