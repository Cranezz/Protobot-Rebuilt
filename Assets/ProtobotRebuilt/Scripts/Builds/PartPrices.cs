using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Parts_List;
using UnityEngine;

namespace Protobot.Builds {
    /// <summary>
    /// V2 (build 79): what parts cost from VEX (Resources/Parts/VEX Prices.txt, read from vexrobotics.com) and what the
    /// team already has. A part's price is its share of a VEX pack: a screw is 1/100 of a 100-pack, a 25-hole C-channel
    /// 25/35 of one of the six 35-hole channels in its pack. Prices the user types and the team's inventory (how many of
    /// each part they own) are kept on this computer (persistentDataPath/Parts/Prices.json, Inventory.json), by part name.
    /// </summary>
    public static class PartPrices {
        public class Rule {
            public Regex pattern;
            public bool byName;
            public string sku, product, stock = "each";
            public float packPrice;
            public int packSize = 1;
            public float stockA, stockB;
        }

        private static List<Rule> rules;
        private static Dictionary<string, float> overrides;
        private static Dictionary<string, int> owned;

        public const string Source = "vexrobotics.com, Nov 2026 (USD)";

        private static string Folder => Path.Combine(Application.persistentDataPath, "Parts");

        private static List<Rule> Rules {
            get {
                if (rules != null) return rules;
                rules = new List<Rule>();
                var text = Resources.Load<TextAsset>("Parts/VEX Prices");
                if (text == null) return rules;
                foreach (var raw in text.text.Split('\n')) {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    var f = Regex.Split(line, @"\s+\|\s+").Select(s => s.Trim()).ToArray();
                    if (f.Length < 6) continue;
                    var rule = new Rule { sku = f[1], product = f[5] };
                    var pattern = f[0];
                    if (pattern.StartsWith("name:")) {
                        rule.byName = true;
                        pattern = pattern.Substring(5);
                    }
                    try { rule.pattern = new Regex(pattern, RegexOptions.CultureInvariant); }
                    catch (ArgumentException) { continue; }
                    float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out rule.packPrice);
                    int.TryParse(f[3], out rule.packSize);
                    rule.packSize = Mathf.Max(1, rule.packSize);
                    var stock = f[4].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    rule.stock = stock.Length > 0 ? stock[0] : "each";
                    if (stock.Length > 1) float.TryParse(stock[1], NumberStyles.Float, CultureInfo.InvariantCulture, out rule.stockA);
                    if (stock.Length > 2) float.TryParse(stock[2], NumberStyles.Float, CultureInfo.InvariantCulture, out rule.stockB);
                    rules.Add(rule);
                }
                return rules;
            }
        }

        public static string IdOf(GameObject part) => part != null && part.TryGetComponent(out SavedObject saved) ? saved.id ?? "" : "";

        /// <summary>The VEX pack a part comes from, and how much of one stock piece it is (1 for a whole part).</summary>
        public static (Rule rule, float share) Find(GameObject part) {
            var id = IdOf(part);
            var name = part != null && part.TryGetComponent(out PartName pn) ? pn.name : "";
            foreach (var rule in Rules) {
                if (!rule.pattern.IsMatch(rule.byName ? name : id)) continue;
                return (rule, Share(rule, id, part));
            }
            return (null, 1f);
        }

        // A cut part's share of its stock piece: holes along it, inches of shaft, or holes of plate area.
        private static float Share(Rule rule, string id, GameObject part = null) {
            var parts = id.Split('-');
            float Number(string s) {
                var m = Regex.Match(s ?? "", @"[\d.]+");
                return m.Success && float.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
            }
            switch (rule.stock) {
                case "holes": {
                    var holes = Number(parts.Last());
                    return holes > 0f && rule.stockA > 0f ? Mathf.Min(1f, holes / rule.stockA) : 1f;
                }
                case "inches": {
                    // V2 (build 88): a string's length to cut (it can take more than one piece of stock)
                    if (part != null && part.TryGetComponent(out Motion.StringLine line) && rule.stockA > 0f) return line.CutLength / rule.stockA;
                    var length = Number(parts.Last());
                    return length > 0f && rule.stockA > 0f ? Mathf.Min(1f, length / rule.stockA) : 1f;
                }
                case "area": {
                    if (parts.Length < 3) return 1f;
                    var area = Number(parts[1]) * Number(parts[2]);
                    var stock = rule.stockA * rule.stockB;
                    return area > 0f && stock > 0f ? Mathf.Min(1f, area / stock) : 1f;
                }
                default: return 1f;
            }
        }

        /// <summary>One part's price from VEX (its share of the pack), or NaN when it has none.</summary>
        public static float VexEach(GameObject part) {
            var (rule, share) = Find(part);
            if (rule == null) return float.NaN;
            var inPack = rule.packSize * (rule.stock == "variety" && rule.stockA > 0f ? rule.stockA : 1f);   // a variety pack: every kind in it
            return rule.packPrice / inPack * share;
        }

        /// <summary>What one costs: the price typed for it, else VEX's (NaN when neither).</summary>
        public static float Each(string partName, GameObject sample) {
            Load();
            return overrides.TryGetValue(partName, out var p) ? p : VexEach(sample);
        }

        public static bool Typed(string partName) {
            Load();
            return overrides.ContainsKey(partName);
        }

        public static void SetPrice(string partName, float? price) {
            Load();
            if (price.HasValue) overrides[partName] = Mathf.Max(0f, price.Value);
            else overrides.Remove(partName);
            Save();
        }

        public static int Owned(string partName) {
            Load();
            return owned.TryGetValue(partName, out var n) ? n : 0;
        }

        public static void SetOwned(string partName, int count) {
            Load();
            if (count > 0) owned[partName] = count;
            else owned.Remove(partName);
            Save();
        }

        public static IReadOnlyDictionary<string, int> Inventory {
            get {
                Load();
                return owned;
            }
        }

        [Serializable] private class Entry { public string name; public float price; public int count; }
        [Serializable] private class Table { public List<Entry> entries = new(); }

        private static void Load() {
            if (overrides != null) return;
            overrides = new Dictionary<string, float>();
            owned = new Dictionary<string, int>();
            try {
                var prices = Path.Combine(Folder, "Prices.json");
                if (File.Exists(prices))
                    foreach (var e in JsonUtility.FromJson<Table>(File.ReadAllText(prices)).entries) overrides[e.name] = e.price;
                var inventory = Path.Combine(Folder, "Inventory.json");
                if (File.Exists(inventory))
                    foreach (var e in JsonUtility.FromJson<Table>(File.ReadAllText(inventory)).entries) owned[e.name] = e.count;
            }
            catch (Exception e) { Debug.LogWarning("[Parts] prices/inventory: " + e.Message); }
        }

        private static void Save() {
            try {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(Path.Combine(Folder, "Prices.json"), JsonUtility.ToJson(new Table { entries = overrides.Select(kv => new Entry { name = kv.Key, price = kv.Value }).ToList() }, true));
                File.WriteAllText(Path.Combine(Folder, "Inventory.json"), JsonUtility.ToJson(new Table { entries = owned.Select(kv => new Entry { name = kv.Key, count = kv.Value }).ToList() }, true));
            }
            catch (Exception e) { Debug.LogWarning("[Parts] saving prices/inventory: " + e.Message); }
        }

        /// <summary>What to order: per VEX product, the packs that cover the parts still needed (cut parts add up their
        /// shares of stock pieces), with the price. Parts without a VEX product are left out.</summary>
        public static List<(Rule rule, int packs, float cost, float units)> Order(IEnumerable<(string name, GameObject sample, int need)> rows) {
            var bySku = new Dictionary<string, (Rule rule, List<float> pieces, Dictionary<string, int> kinds)>();
            foreach (var (name, sample, need) in rows) {
                if (need <= 0 || sample == null) continue;
                var (rule, share) = Find(sample);
                if (rule == null) continue;
                if (!bySku.TryGetValue(rule.sku, out var had)) bySku[rule.sku] = had = (rule, new List<float>(), new Dictionary<string, int>());
                for (var i = 0; i < need; i++) had.pieces.Add(share);
                had.kinds[name] = (had.kinds.TryGetValue(name, out var k) ? k : 0) + need;
            }
            return bySku.Values.Select(v => {
                // A variety pack has packSize of each kind: as many packs as the kind most needed takes.
                var units = v.rule.stock == "variety" ? v.kinds.Values.Max() : Units(v.rule, v.pieces);
                var packs = Mathf.CeilToInt(units / v.rule.packSize - 1e-4f);
                return (v.rule, packs, packs * v.rule.packPrice, units);
            }).OrderByDescending(o => o.Item3).ToList();
        }

        // How many stock pieces the parts need: cut lengths are packed into whole sticks (first fit, longest first: two
        // 25-hole channels can't come out of one 35-hole stick); whole parts and plate areas just add up.
        private static float Units(Rule rule, List<float> pieces) {
            if (rule.stock != "holes" && rule.stock != "inches") return pieces.Sum();
            var sticks = new List<float>();
            foreach (var whole in pieces.OrderByDescending(p => p)) {
                // V2 (build 88): a piece longer than the stock (a long string) takes whole pieces, then the rest
                var piece = whole;
                while (piece > 1f + 1e-4f) {
                    sticks.Add(0f);
                    piece -= 1f;
                }
                var i = sticks.FindIndex(left => left + 1e-4f >= piece);
                if (i >= 0) sticks[i] -= piece;
                else sticks.Add(1f - piece);
            }
            return sticks.Count;
        }
    }
}
