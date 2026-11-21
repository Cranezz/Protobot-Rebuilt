// Part type ids and their first options for a few groups (ANGL, PLTE, OMNI, SNSR, CLMP, SPCR, SHFT, BAND...).
var sb = new System.Text.StringBuilder();
foreach (var t in Protobot.PartsManager.partTypes) {
    if (!new[] { "ANGL", "PLTE", "OMNI", "SNSR", "CLMP", "SPCR", "SHFT", "BAND", "LOOP", "RBND", "SNDF", "BRNG", "WHEL", "TWHL" }.Any(p => t.id.StartsWith(p))) continue;
    var g = t.GetComponent<Protobot.PartGenerator>();
    sb.AppendLine(t.id + ": " + (g != null ? (g.UsesParams ? string.Join(",", g.GetParam1Options().Take(14)) : "") + " | " + (g.UsesTwoParams ? string.Join(",", g.GetParam2Options().Take(10)) : "") : ""));
}
return sb.ToString();
