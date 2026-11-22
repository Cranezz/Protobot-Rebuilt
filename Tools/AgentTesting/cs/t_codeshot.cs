// (Play) Trailer code shot: the Autonomous square example filled in for the open robot, the code window open, then Run.
var cmd = System.IO.File.ReadAllText("Temp/codeshot.txt").Trim();
if (cmd.StartsWith("open")) {
    var index = cmd.Length > 5 ? int.Parse(cmd.Substring(5)) : 3;
    var code = Protobot.RobotCode.Examples.Fill(Protobot.RobotCode.Examples.All[index].code);
    Protobot.RobotCode.CodeStore.Set(code);
    Protobot.RobotCode.CodePanel.Open();
    return "open, " + code.Split('\n').Length + " lines";
}
if (cmd == "run") {
    foreach (var b in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(UnityEngine.FindObjectsSortMode.None))
        if (b.name == "Run" && b.gameObject.activeInHierarchy && b.GetComponentInParent<Protobot.RobotCode.CodePanel>() != null) { b.onClick.Invoke(); return "run pressed"; }
    return "no Run button";
}
if (cmd == "nodevices") { foreach (var mb in UnityEngine.Object.FindObjectsByType<UnityEngine.MonoBehaviour>(UnityEngine.FindObjectsSortMode.None)) if (mb.GetType().Name == "DevicesPanel") UnityEngine.Object.Destroy(mb.gameObject); return "devices closed"; }
if (cmd == "close") { Protobot.RobotCode.CodePanel.Close(); if (Protobot.Motion.Sandbox.Running) Protobot.Motion.Sandbox.Stop(); return "closed"; }
return "?";
