// Which simulation body each of the odometry parts is on (count of parts in that body).
var sb = new System.Text.StringBuilder();
foreach (var b in Protobot.Motion.Sandbox.Instance.Bodies()) {
    var ids = b.nodes.Select(n => n.GetComponent<Protobot.SavedObject>()?.id ?? n.name).ToList();
    if (ids.Any(i => i.StartsWith("SNSR") || i.StartsWith("PLTE-2-6") || i.StartsWith("ANGL-3x3")) || ids.Count < 3)
        sb.AppendLine(ids.Count + " parts, mass " + b.rb.mass.ToString("F3") + ": " + string.Join(",", ids.Where(i => i.StartsWith("SNSR") || i.StartsWith("PLTE-2-6") || i.StartsWith("ANGL") || i.StartsWith("CCHL")).Distinct()) + (ids.Count < 3 ? " | " + string.Join(",", ids) : ""));
}
return sb.ToString();
