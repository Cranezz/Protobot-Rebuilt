using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Parts_List;
using Protobot.Interface;
using SFB;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Builds {
    // V2: File > Parts list…: every part in the build with how many, the weight of one and of all of them, sorted by
    // count, name or weight; or by assembly (each set of parts fastened together, with its own weight: the drive,
    // the intake, the lift...). Export CSV makes a spreadsheet of it (Excel, Google Sheets); Export text… is the
    // original's text list (it was File > Export parts list).
    public static class PartsListWindow {
        /// <summary>The original's text export (set by the top bar, which holds its hidden button).</summary>
        public static System.Action TextExport;

        private enum Sort { Count, Name, Weight }

        private static Sort sort = Sort.Count;
        private static bool byAssembly;
        private static bool cost;   // V2 (build 79): what it costs and what to buy, instead of weights

        private struct Row {
            public string name;
            public int count;
            public float each;   // lb
            public GameObject sample;
        }

        public static void Open() {
            var dialog = Dialog.Open("Parts list", new Vector2(780f, 640f));
            var body = dialog.Body;
            Ui.Column(body, 8f);
            var top = Ui.Rect("Top", body);
            Ui.Row(top, 8f);
            top.Size(-1, 34f);
            var summary = Ui.Text(body, "Summary", "", Theme.FontBody, Theme.Text, Theme.Medium);
            summary.Size(-1, 26f);
            var frame = Ui.Image(body, "List", Theme.Field, 8f);
            frame.Size(-1, -1).GetComponent<LayoutElement>().flexibleHeight = 1f;
            var viewport = frame.rectTransform;
            viewport.gameObject.AddComponent<RectMask2D>();
            var list = Ui.Rect("Rows", viewport);
            list.anchorMin = new Vector2(0f, 1f);
            list.anchorMax = new Vector2(1f, 1f);
            list.pivot = new Vector2(0.5f, 1f);
            list.sizeDelta = Vector2.zero;
            Ui.Column(list, 1f, new RectOffset(8, 8, 6, 6));
            list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = list;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            void Fill() {
                foreach (Transform child in top) Object.Destroy(child.gameObject);
                foreach (Transform child in list) Object.Destroy(child.gameObject);
                ToolOptions.Segmented(top, new[] { "Weight", "Cost" }, cost ? 1 : 0, i => {
                    cost = i == 1;
                    Fill();
                });
                ToolOptions.Note(top, "Sort by");
                ToolOptions.Segmented(top, new[] { "Count", "Name", cost ? "Cost" : "Weight" }, (int)sort, i => {
                    sort = (Sort)i;
                    Fill();
                });
                if (cost) {
                    CostView(list, summary, Fill);
                    return;
                }
                ToolOptions.Switch(top, "By assembly", byAssembly, on => {
                    byAssembly = on;
                    Fill();
                });
                var groups = byAssembly ? Assemblies() : new List<(string, List<GameObject>)> { ("", Parts()) };
                var all = Parts();
                summary.text = all.Count + " parts, " + Rows(all).Count + " kinds, " + Pounds(Rows(all).Sum(r => r.each * r.count))
                               + (byAssembly ? " in " + groups.Count + (groups.Count == 1 ? " assembly" : " assemblies") : "");
                foreach (var (title, parts) in groups) {
                    var rows = Sorted(Rows(parts));
                    if (byAssembly) Header(list, title + "  ·  " + parts.Count + " parts, " + Pounds(rows.Sum(r => r.each * r.count)));
                    else Header(list, "Part", "Count", "Each", "Total");
                    foreach (var row in rows) Line(list, row.name, "x" + row.count, row.each > 0f ? Pounds(row.each, 3) : "?", row.each > 0f ? Pounds(row.each * row.count) : "?");
                }
            }
            Fill();

            if (TextExport != null) {
                var text = Ui.Button(dialog.Footer, "Text", "Export text…", TextExport, false, 6f);
                text.Size(130f, 34f);
                Ui.Tip(text, "Export text", null, "The parts as a plain text list, like the original Protobot's.");
            }
            var order = Ui.Button(dialog.Footer, "Order", "What to order…", OrderDialog, false, 6f);
            order.Size(150f, 34f);
            Ui.Tip(order, "What to order", null, "The VEX products (and how many packs) that cover the parts you don't have yet, with the price; export it as a spreadsheet.");
            var csv = Ui.Button(dialog.Footer, "CSV", "Export CSV…", ExportCsv, false, 6f);
            csv.Size(130f, 34f);
            Ui.Tip(csv, "Export CSV", null, "A spreadsheet of the parts (Excel, Google Sheets): name, count, weight each and in all, and each part's assembly.");
            var done = Ui.Button(dialog.Footer, "Done", "Done", dialog.Close, true, 6f);
            done.Size(110f, 34f);
        }

        private static List<GameObject> Parts() =>
            PartsManager.FindLoadedObjects().Where(p => p != null && !p.IsDeleted() && p.GetComponent<PartName>() != null).ToList();

        private static string NameOf(GameObject part) => part.GetComponent<PartName>().name;

        private static List<Row> Rows(List<GameObject> parts) =>
            parts.GroupBy(NameOf).Select(g => new Row { name = g.Key, count = g.Count(), each = g.First().GetComponent<PartName>().GetWeight(), sample = g.First() }).ToList();

        private static List<Row> Sorted(List<Row> rows) => sort switch {
            Sort.Name => rows.OrderBy(r => r.name).ToList(),
            Sort.Weight when cost => rows.OrderByDescending(r => Price(r) * r.count).ThenBy(r => r.name).ToList(),
            Sort.Weight => rows.OrderByDescending(r => r.each * r.count).ThenBy(r => r.name).ToList(),
            _ => rows.OrderByDescending(r => r.count).ThenBy(r => r.name).ToList(),
        };

        // Each set of parts joined together in any way, largest first, named by what it's mostly made of (not its
        // screws and nuts).
        private static List<(string title, List<GameObject> parts)> Assemblies() {
            var left = new HashSet<GameObject>(Parts());
            var groups = new List<List<GameObject>>();
            while (left.Count > 0) {
                // Everything joined to it in any way (tight, on a joint, spinning on a screw or shaft).
                var first = left.First();
                var group = new List<GameObject> { first };
                left.Remove(first);
                for (var i = 0; i < group.Count; i++)
                    foreach (var next in GroupManager.Neighbours(group[i]))
                        if (next != null && left.Remove(next)) group.Add(next);
                groups.Add(group);
            }
            return groups.OrderByDescending(g => g.Count).Select((g, i) => {
                var motors = g.Count(p => p.GetComponent<Motion.MotorDrive>() != null);
                bool Hardware(string n) => n.Contains("Screw") || n.Contains("Nut") || n.Contains("Spacer") || n.Contains("Washer") || n.Contains("Collar");
                var named = g.Where(p => !Hardware(PartLabels.Short(p))).DefaultIfEmpty(g[0]);
                var main = named.GroupBy(p => PartLabels.Short(p)).OrderByDescending(x => x.Count()).First().Key;
                var title = g.Count == 1 ? PartLabels.Short(g[0]) : "Assembly " + (i + 1) + " (" + main + (motors > 0 ? ", " + motors + (motors == 1 ? " motor" : " motors") : "") + ")";
                return (title, g);
            }).ToList();
        }

        private static string Pounds(float lb, int decimals = 2) => lb.ToString("F" + decimals, CultureInfo.InvariantCulture) + " lb";

        private static void Header(RectTransform list, string a, string b = null, string c = null, string d = null) {
            var row = Line(list, a, b ?? "", c ?? "", d ?? "", true);
            row.Size(-1, 30f);
        }

        private static RectTransform Line(RectTransform list, string name, string count, string each, string total, bool header = false) {
            var row = Ui.Rect("Row", list);
            Ui.Row(row, 8f, new RectOffset(4, 4, 0, 0));
            row.Size(-1, 24f);
            var color = header ? Theme.TextDim : Theme.Text;
            var size = header ? Theme.FontSmall : Theme.FontBody;
            var n = Ui.Text(row, "Name", name, size, color, header ? Theme.Medium : null);
            n.Size(-1, 24f, 1f);
            n.overflowMode = TextOverflowModes.Ellipsis;
            foreach (var (text, width) in new[] { (count, 70f), (each, 110f), (total, 110f) }) {
                var t = Ui.Text(row, "Value", text, size, color, null, TextAlignmentOptions.MidlineRight);
                t.Size(width, 24f);
            }
            return row;
        }

        private static void ExportCsv() {
            var path = StandaloneFileBrowser.SaveFilePanel("Export parts list", "", ModelExport.BuildName() + " parts", "csv");
            InputEvents.TextInput.AfterFileWindow();
            if (string.IsNullOrEmpty(path)) {
                Interface.Interface.Say("Export cancelled", false);
                return;
            }
            var sb = new StringBuilder();
            sb.AppendLine("Assembly,Part,Count,Weight each (lb),Weight total (lb)");
            var total = 0f;
            foreach (var (title, parts) in Assemblies())
                foreach (var row in Sorted(Rows(parts))) {
                    sb.AppendLine(Csv(title) + "," + Csv(row.name) + "," + row.count + "," + row.each.ToString("0.####", CultureInfo.InvariantCulture) + ","
                                  + (row.each * row.count).ToString("0.####", CultureInfo.InvariantCulture));
                    total += row.each * row.count;
                }
            sb.AppendLine(",Total,," + "," + total.ToString("0.###", CultureInfo.InvariantCulture));
            try {
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                Interface.Interface.Announce("Saved the parts list as a spreadsheet: " + Path.GetFileName(path));
            }
            catch (IOException e) {
                Interface.Interface.Say("Couldn't save it (is it open in Excel?): " + e.Message, false);
            }
        }

        // ------------------------------------------------------------------ V2 (build 79): cost and inventory

        private static float Price(Row r) {
            var p = PartPrices.Each(r.name, r.sample);
            return float.IsNaN(p) ? 0f : p;
        }

        private static string Money(float v) => "$" + v.ToString("N2", CultureInfo.InvariantCulture);

        private static void CostView(RectTransform list, TextMeshProUGUI summary, System.Action refill) {
            var rows = Sorted(Rows(Parts()));
            float total = 0f, buy = 0f;
            int unpriced = 0, needed = 0;
            CostHeader(list);
            foreach (var row in rows) {
                var each = PartPrices.Each(row.name, row.sample);
                var have = PartPrices.Owned(row.name);
                var need = Mathf.Max(0, row.count - have);
                if (float.IsNaN(each)) unpriced++;
                else {
                    total += each * row.count;
                    buy += each * need;
                }
                needed += need;
                CostLine(list, row, each, have, need, refill);
            }
            var packs = PartPrices.Order(rows.Select(r => (r.name, r.sample, Mathf.Max(0, r.count - PartPrices.Owned(r.name))))).Sum(o => o.cost);
            summary.text = "Robot " + Money(total) + " · to get: " + needed + " parts, " + Money(buy) + " (" + Money(packs) + " in VEX packs)"
                           + (unpriced > 0 ? " · " + unpriced + " unpriced" : "");
            var note = Ui.Text(list, "Source", "Prices: " + PartPrices.Source + ", each part's share of its pack (a cut channel: its share of the 35-hole one). Click a price or an owned count to change it: your team's parts are kept on this computer for every build.", Theme.FontSmall - 1f, Theme.TextDim);
            note.textWrappingMode = TextWrappingModes.Normal;
            note.Size(-1, 40f);
        }

        private static void CostHeader(RectTransform list) {
            var row = Ui.Rect("Header", list);
            Ui.Row(row, 8f, new RectOffset(4, 4, 0, 0));
            row.Size(-1, 30f);
            var n = Ui.Text(row, "Name", "Part", Theme.FontSmall, Theme.TextDim, Theme.Medium);
            n.Size(-1, 24f, 1f);
            foreach (var (text, width) in new[] { ("Count", 56f), ("Owned", 66f), ("Each", 80f), ("To buy", 96f) }) {
                var t = Ui.Text(row, "Value", text, Theme.FontSmall, Theme.TextDim, null, TextAlignmentOptions.MidlineRight);
                t.Size(width, 24f);
            }
        }

        private static void CostLine(RectTransform list, Row r, float each, int have, int need, System.Action refill) {
            var row = Ui.Rect("Row", list);
            Ui.Row(row, 8f, new RectOffset(4, 4, 0, 0));
            row.Size(-1, 26f);
            var n = Ui.Text(row, "Name", r.name, Theme.FontBody, Theme.Text);
            n.Size(-1, 24f, 1f);
            n.overflowMode = TextOverflowModes.Ellipsis;
            var (rule, _) = PartPrices.Find(r.sample);
            if (rule != null) Ui.Tip(n, r.name, null, "VEX " + rule.sku + ": " + rule.product + ", " + Money(rule.packPrice) + ".");
            var c = Ui.Text(row, "Count", "x" + r.count, Theme.FontBody, Theme.Text, null, TextAlignmentOptions.MidlineRight);
            c.Size(56f, 24f);
            var owned = Ui.Field(row, "Owned", have > 0 ? have.ToString(CultureInfo.InvariantCulture) : "", v => {
                PartPrices.SetOwned(r.name, int.TryParse(v, out var k) ? k : 0);
                refill();
            }, Theme.FontSmall, TextAlignmentOptions.MidlineRight);
            owned.Size(66f, 24f);
            Ui.Tip(owned, "Owned", null, "How many of these your team has (kept for every build): they're left out of what to buy.");
            var price = Ui.Field(row, "Each", float.IsNaN(each) ? "" : each.ToString(each < 1f ? "0.000" : "0.00", CultureInfo.InvariantCulture), v => {
                v = (v ?? "").Replace("$", "").Trim();
                PartPrices.SetPrice(r.name, float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : (float?)null);
                refill();
            }, Theme.FontSmall, TextAlignmentOptions.MidlineRight);
            price.Size(80f, 24f);
            Ui.Tip(price, "Price each", null, (PartPrices.Typed(r.name) ? "Your price (empty it for VEX's)." : rule != null ? "VEX's price for one (its share of the pack)." : "No VEX price: type what one costs.") + " US dollars.");
            var t = Ui.Text(row, "To buy", need == 0 ? "have all" : float.IsNaN(each) ? need + " · ?" : need + " · " + Money(each * need), Theme.FontSmall, need == 0 ? Theme.Success : Theme.Text, null, TextAlignmentOptions.MidlineRight);
            t.Size(96f, 24f);
        }

        // The VEX products (packs) that cover what the team doesn't have.
        private static void OrderDialog() {
            var rows = Rows(Parts());
            var order = PartPrices.Order(rows.Select(r => (r.name, r.sample, Mathf.Max(0, r.count - PartPrices.Owned(r.name)))));
            var missing = rows.Where(r => r.count > PartPrices.Owned(r.name) && PartPrices.Find(r.sample).rule == null).ToList();
            var dialog = Dialog.Open("What to order", new Vector2(720f, 600f));
            var body = dialog.Body;
            Ui.Column(body, 6f);
            var head = Ui.Text(body, "Summary", order.Count == 0 ? "Nothing to order from VEX: you have every part (or the rest have no VEX product)."
                : order.Sum(o => o.packs) + " packs from VEX: " + Money(order.Sum(o => o.cost)) + " before tax and shipping (" + PartPrices.Source + ")", Theme.FontBody, Theme.Text, Theme.Medium);
            head.Size(-1, 26f);
            var frame = Ui.Image(body, "List", Theme.Field, 8f);
            frame.Size(-1, -1).GetComponent<LayoutElement>().flexibleHeight = 1f;
            frame.gameObject.AddComponent<RectMask2D>();
            var list = Ui.Rect("Rows", frame.rectTransform);
            list.anchorMin = new Vector2(0f, 1f);
            list.anchorMax = new Vector2(1f, 1f);
            list.pivot = new Vector2(0.5f, 1f);
            list.sizeDelta = Vector2.zero;
            Ui.Column(list, 1f, new RectOffset(8, 8, 6, 6));
            list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = frame.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = frame.rectTransform;
            scroll.content = list;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            Header(list, "VEX product", "SKU", "Packs", "Cost");
            foreach (var o in order) Line(list, o.rule.product, o.rule.sku, "x" + o.packs, Money(o.cost));
            if (missing.Count > 0) {
                var none = Ui.Text(list, "Missing", "No VEX product (buy elsewhere or type a price): " + string.Join(", ", missing.Select(m => m.name + " x" + (m.count - PartPrices.Owned(m.name)))), Theme.FontSmall, Theme.TextDim);
                none.textWrappingMode = TextWrappingModes.Normal;
                none.Size(-1, 44f);
            }
            var export = Ui.Button(dialog.Footer, "Export", "Export CSV…", () => {
                var path = StandaloneFileBrowser.SaveFilePanel("Export order", "", ModelExport.BuildName() + " order", "csv");
                InputEvents.TextInput.AfterFileWindow();
                if (string.IsNullOrEmpty(path)) return;
                var sb = new StringBuilder();
                sb.AppendLine("SKU,Product,Packs,Pack price (USD),Cost (USD)");
                foreach (var o in order)
                    sb.AppendLine(o.rule.sku + "," + Csv(o.rule.product) + "," + o.packs + "," + o.rule.packPrice.ToString("0.00", CultureInfo.InvariantCulture) + ","
                                  + o.cost.ToString("0.00", CultureInfo.InvariantCulture));
                foreach (var m in missing) sb.AppendLine("," + Csv(m.name + " (no VEX product)") + "," + (m.count - PartPrices.Owned(m.name)) + ",,");
                sb.AppendLine(",Total,,," + order.Sum(o => o.cost).ToString("0.00", CultureInfo.InvariantCulture));
                try {
                    File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                    Interface.Interface.Announce("Saved the order list: " + Path.GetFileName(path));
                }
                catch (IOException e) { Interface.Interface.Say("Couldn't save it (is it open in Excel?): " + e.Message, false); }
            }, false, 6f);
            export.Size(140f, 34f);
            var done = Ui.Button(dialog.Footer, "Done", "Done", dialog.Close, true, 6f);
            done.Size(110f, 34f);
        }

        private static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }
}
