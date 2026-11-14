var sb = new System.Text.StringBuilder();
foreach (var name in new[] { "Sensor", "Switch" }) {
    UnityEngine.GameObject go = null;
    foreach (var g in UnityEngine.Resources.LoadAll<UnityEngine.GameObject>("Part Prefabs")) if (g.name == name) go = g;
    var gen = go.GetComponent<Protobot.PartGenerator>();
    var keep = gen.param1.value;
    foreach (var o in gen.GetParam1Options()) {
        gen.param1.value = o;
        sb.Append(name + " " + o + ": ");
        if (gen.UsesTwoParams) foreach (var o2 in gen.GetParam2Options()) sb.Append(o2 + ", ");
        sb.AppendLine();
    }
    gen.param1.value = keep;
}
return sb.ToString();
