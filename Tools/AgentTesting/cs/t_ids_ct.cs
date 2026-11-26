// (Play) lists part type options for some ids (Temp/ids.txt: prefixes, one per line)
var want = System.IO.File.ReadAllLines("Temp/ids.txt");
var sb = new System.Text.StringBuilder();
foreach (var type in Resources.LoadAll<Protobot.PartType>("Part Prefabs").Concat(UnityEngine.Object.FindObjectsByType<Protobot.PartType>(FindObjectsInactive.Include, FindObjectsSortMode.None)).Distinct()) {
    if (!want.Any(w => type.id.StartsWith(w))) continue;
    var gen = type.GetComponent<Protobot.PartGenerator>();
    var p1 = gen.GetParam1Options(); var p2 = gen.GetParam2Options();
    sb.AppendLine(type.id + ": p1 [" + string.Join("; ", p1.Take(60)) + "] p2 [" + string.Join("; ", p2.Take(30)) + "]");
}
return sb.ToString();
