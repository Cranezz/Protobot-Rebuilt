// (Play, a build loaded) the parts list's cost: each kind with its VEX product, price each and count; the total; the
// order (packs). Temp/cost.txt "shot" opens the parts list on Cost and saves Temp/cost.png.
var cmd = System.IO.File.Exists("Temp/cost.txt") ? System.IO.File.ReadAllText("Temp/cost.txt").Trim() : "";
var parts = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(Protobot.PartsManager.FindLoadedObjects(), p => p != null && p.GetComponent<Parts_List.PartName>() != null));
var sb = new System.Text.StringBuilder();
var total = 0f; var none = new System.Collections.Generic.List<string>();
var rows = new System.Collections.Generic.List<(string, GameObject, int)>();
foreach (var g in System.Linq.Enumerable.GroupBy(parts, p => p.GetComponent<Parts_List.PartName>().name)) {
    var sample = System.Linq.Enumerable.First(g); var n = System.Linq.Enumerable.Count(g);
    var each = Protobot.Builds.PartPrices.Each(g.Key, sample);
    var r = Protobot.Builds.PartPrices.Find(sample);
    if (float.IsNaN(each)) none.Add(g.Key + " [" + Protobot.Builds.PartPrices.IdOf(sample) + "] x" + n);
    else { total += each * n; sb.AppendLine(n + " x " + g.Key + " = " + each.ToString("0.000") + " (" + r.rule.sku + " share " + r.share.ToString("0.00") + ")"); }
    rows.Add((g.Key, sample, n));
}
var order = Protobot.Builds.PartPrices.Order(rows);
sb.AppendLine("TOTAL parts $" + total.ToString("0.00") + " | order " + System.Linq.Enumerable.Sum(order, o => o.packs) + " packs $" + System.Linq.Enumerable.Sum(order, o => o.cost).ToString("0.00"));
foreach (var o in order) sb.AppendLine("  " + o.packs + " x " + o.rule.sku + " " + o.rule.product + " = $" + o.cost.ToString("0.00") + " (units " + o.units.ToString("0.00") + ")");
sb.AppendLine("NO PRICE: " + string.Join("; ", none));
if (cmd == "shot") {
    var t = typeof(Protobot.Builds.PartsListWindow);
    t.GetField("cost", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).SetValue(null, true);
    Protobot.Builds.PartsListWindow.Open();
    UnityEngine.ScreenCapture.CaptureScreenshot(System.IO.Path.GetFullPath("Temp/cost.png"));
}
return sb.ToString();
