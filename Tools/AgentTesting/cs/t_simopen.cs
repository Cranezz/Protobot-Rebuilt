// (Play, the demo robot loaded) Temp/simopen.txt: "sim" starts the simulation; "open" opens another build over it (as
// File > Open does) and says whether the simulation stopped; "check" counts NullReferenceExceptions logged since "sim";
// "catalog" says whether the parts list still offers a V5 Smart Cable.
var cmd = System.IO.File.ReadAllText("Temp/simopen.txt").Trim();
if (cmd == "sim") {
    System.AppDomain.CurrentDomain.SetData("nre", 0);
    if (System.AppDomain.CurrentDomain.GetData("nreHook") == null) {
        UnityEngine.Application.LogCallback hook = (m, st, ty) => { if (m.Contains("NullReferenceException")) System.AppDomain.CurrentDomain.SetData("nre", (int)System.AppDomain.CurrentDomain.GetData("nre") + 1); };
        UnityEngine.Application.logMessageReceived += hook;
        System.AppDomain.CurrentDomain.SetData("nreHook", hook);
    }
    Protobot.Motion.Sandbox.Toggle();
    return "running " + Protobot.Motion.Sandbox.Running;
}
if (cmd == "open") {
    Protobot.Builds.SceneBuild.GenerateBuild(Protobot.Builds.BuildFile.Read(@"C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Sample Builds/6 Motor Drive with intake.pbb"));
    return "opened; simulation running " + Protobot.Motion.Sandbox.Running;
}
if (cmd == "catalog") {
    System.Func<Protobot.Interface.CatalogNode, bool> smart = null;
    smart = n => n.action == "wire:V5" || (n.children != null && n.children.Any(c => smart(c)));
    return "smart cable offered: " + Protobot.Interface.PartsCatalog.Nodes.Any(n => smart(n));
}
return "NullReferenceExceptions since sim: " + System.AppDomain.CurrentDomain.GetData("nre");

