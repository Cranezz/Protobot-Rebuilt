using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Protobot.Interface {
    // V2: how the parts browser is organised: sections in the order teams reach for them (hardware first: screws
    // and nuts are needed all the time), and in each section the parts from most to least common. Families
    // (wheels, gears, the Advanced Gear Kit, pneumatics...) are groups: one tile that opens a menu of its parts,
    // with sub-menus where a family has kinds and sizes (Wheels > Omni > 4"). Every part type is in here once; the
    // VEX-CAD library's families sit next to the original's parts they go with.
    public class CatalogNode {
        public string name;
        public string section;
        public string typeId;           // a part: its type...
        public string p1, p2;           // ...and the sizes it starts with (null: the type's own)
        public string iconType;         // whose icon a group shows
        public string action;           // "import", "polycarb", "sheets", "chain"
        public string customId;         // a custom part
        public List<CatalogNode> children;
        public string tip;

        public bool IsGroup => children != null && children.Count > 0;
        public string IconType => iconType ?? typeId ?? children?.Select(c => c.IconType).FirstOrDefault(t => t != null);

        /// <summary>Every part in this node (itself, or all under it).</summary>
        public IEnumerable<CatalogNode> Leaves() {
            if (!IsGroup) {
                yield return this;
                yield break;
            }
            foreach (var child in children)
                foreach (var leaf in child.Leaves())
                    yield return leaf;
        }

        public int Count => IsGroup ? Leaves().Count() : 1;
    }

    public static class PartsCatalog {
        public static readonly string[] Sections = { "Hardware", "Structure", "Motion", "Electronics", "Pneumatics", "Field", "Custom" };

        private static List<CatalogNode> nodes;
        private static Dictionary<string, PartType> types;

        public static void Invalidate() => nodes = null;

        /// <summary>The top-level tiles, in order (custom parts included).</summary>
        public static List<CatalogNode> Nodes {
            get {
                if (nodes == null) Build();
                return nodes;
            }
        }

        /// <summary>A rendered picture for a VEX-CAD part, or a group led by one (null: use the type's icon).</summary>
        public static UnityEngine.Texture2D ModelIcon(CatalogNode node) {
            // V2: bands and tubing are drawn (they have no model); a family of them shows its first.
            var drawn = node == null ? null : node.IsGroup ? node.Leaves().FirstOrDefault(l => l.action != null)?.action : node.action;
            if (drawn != null && Protobot.Motion.StrandIcons.For(drawn) is UnityEngine.Texture2D strand) return strand;
            if (node == null || node.IconType == null || !IsVex(node.IconType)) return null;
            var leaf = node.IsGroup ? node.Leaves().FirstOrDefault(l => l.typeId == node.IconType && l.p1 != null) : node;
            return leaf != null && leaf.p1 != null && IsVex(leaf.typeId) ? Protobot.Custom.Thumbnails.ForVexPart(leaf.typeId, leaf.p1) : null;
        }

        public static PartType Type(string id) => types != null && id != null && types.TryGetValue(id, out var type) ? type : null;

        private static void Build() {
            types = PartsManager.partTypes.Where(t => t != null).GroupBy(t => t.id).ToDictionary(g => g.Key, g => g.First());
            nodes = new List<CatalogNode>();
            Hardware();
            Structure();
            Motion();
            Electronics();
            Pneumatics();
            Field();
            Custom();
            // Anything not placed above (a new part type) still shows, at the end of its group's section.
            var used = new HashSet<string>(nodes.SelectMany(n => n.Leaves()).Select(l => l.typeId).Where(t => t != null));
            foreach (var type in types.Values) {
                if (used.Contains(type.id) || Hidden.Contains(type.id) || type.id == Protobot.Custom.CustomParts.Imported || type.id == Protobot.Custom.CustomParts.Polycarbonate) continue;
                var section = type.group switch {
                    PartType.PartGroup.Structure => "Structure",
                    PartType.PartGroup.Motion => "Motion",
                    PartType.PartGroup.Electronics => "Electronics",
                    _ => "Field",
                };
                nodes.Add(IsVex(type.id) ? Family(section, type.name, type.id) : Leaf(section, type.name, type.id));
            }
        }

        // ------------------------------------------------------------------ helpers

        public static bool IsVex(string id) => id != null && id.StartsWith("VX");
        private static bool Has(string id) => types.ContainsKey(id);

        private static List<string> Options1(string id) {
            if (!Has(id)) return new List<string>();
            var generator = types[id].GetComponent<PartGenerator>();
            return generator.UsesParams && !generator.param1.custom ? generator.GetParam1Options() ?? new List<string>() : new List<string>();
        }

        private static List<string> Options2(string id, string p1) {
            if (!Has(id)) return new List<string>();
            var generator = types[id].GetComponent<PartGenerator>();
            if (!generator.UsesTwoParams || generator.param2.custom) return new List<string>();
            var keep = generator.param1.value;
            generator.param1.value = p1;
            var options = generator.GetParam2Options() ?? new List<string>();
            generator.param1.value = keep;
            return options;
        }

        /// <summary>"1/4in" → 1/4", "2.75in" → 2.75".</summary>
        public static string Pretty(string option) => Regex.Replace(option ?? "", @"(\d)in\b", "$1\"");

        private static CatalogNode Leaf(string section, string name, string typeId, string p1 = null, string p2 = null, string tip = null) =>
            Has(typeId) ? new CatalogNode { section = section, name = name, typeId = typeId, p1 = p1, p2 = p2, tip = tip } : null;

        private static CatalogNode Group(string section, string name, string iconType, params CatalogNode[] children) {
            var list = children.Where(c => c != null && (!c.IsGroup || c.children.Count > 0) && (c.IsGroup || c.typeId != null || c.action != null)).ToList();
            if (list.Count == 0) return null;
            if (list.Count == 1 && !list[0].IsGroup && list[0].typeId != null && name != "Retainers")
                return list[0];  // V2: a menu of one is just that part
            return new CatalogNode { section = section, name = name, iconType = iconType, children = list };
        }

        private static CatalogNode Group(string section, string name, string iconType, IEnumerable<CatalogNode> children) =>
            Group(section, name, iconType, children.ToArray());

        /// <summary>One leaf per first-parameter option of a type.</summary>
        private static IEnumerable<CatalogNode> Each(string section, string typeId, Func<string, string> label = null, Func<string, bool> filter = null) =>
            Options1(typeId).Where(o => filter == null || filter(o)).Select(o => Leaf(section, label != null ? label(o) : Pretty(o), typeId, o));

        /// <summary>One leaf per second-parameter option (sizes) of a type's first option.</summary>
        private static IEnumerable<CatalogNode> Sizes(string section, string typeId, string p1, Func<string, string> label = null) =>
            Options2(typeId, p1).Select(o => Leaf(section, label != null ? label(o) : Pretty(o), typeId, p1, o));

        /// <summary>A VEX-CAD family as a group of its parts (optionally only some).</summary>
        private static CatalogNode Family(string section, string name, string typeId, Func<string, bool> filter = null) =>
            Group(section, name, typeId, Each(section, typeId, o => o, filter));

        private static void Add(CatalogNode node) {
            if (node != null) nodes.Add(node);
        }

        private static bool Contains(string text, params string[] words) => words.Any(w => text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);

        // ------------------------------------------------------------------ the sections

        // V2 (catalog audit): each part shows once. Where the original has a part (with its screw/nut/shaft
        // behaviour), the VEX-CAD model of the same part is left out; VEX models only add what the original lacks
        // (other sizes, materials, kinds). Generic template models ("Nylon Spacer (5/16" OD)" with no length),
        // "threads shown" copies, VEX IQ parts and full-length stock (every channel is cut from full length with
        // the saw) are left out too; Hidden keeps them from coming back at the end of a section.
        private static readonly HashSet<string> Hidden = new() {
            "OVSG", "OVTG", "OVAG",   // V2: duplicates of OVGL's goals
            "BAND", "PTUB", "WIRE",   // V2: bands, tubing and cables are made by their tools (listed as actions)
            "CHAN", "VXCCHANNELSTOCK", "VXANGLESTOCK", "VXPLATESTOCK", "VXSLIDEASSEMBLYP", "VXSTANDOFFVEX", "VXV5ELECTRONICS",
            "WSHR", "VXCHAIN9P", "VXGUSSETSVEX",   // V2: replaced by the new washers, made by the Chain tool, the original's gussets
        };

        private static bool Template(string o) => Regex.IsMatch(o, @"^(Nylon Spacer|Spacer|Washer) ") || Regex.IsMatch(o, @"^[\d.]+"" Flex Wheel$");
        private static bool Duplicate(string o) => Contains(o, "threads shown", "VEX IQ", "IQ Chain", "(old)");

        /// <summary>VEX family parts (minus templates and copies), each with a friendly name.</summary>
        private static IEnumerable<CatalogNode> Vex(string section, string typeId, Func<string, bool> filter = null, Func<string, string> label = null) =>
            Each(section, typeId, label ?? (o => o), o => !Template(o) && !Duplicate(o) && (filter == null || filter(o)));

        // V2 (2026-11-10, the user's list): every family is one tile with its kinds as sub-groups and the sizes in
        // them; removed duplicates (the 4" omni (locked), the second V2 traction set, the 4" traction wheel (169),
        // the bent rubber link, the old generic washers, chains and treads (the Chain tool makes them), inserts and
        // adapters (they go in by themselves), the 25-hole segmented angle).
        private static void Hardware() {
            const string s = "Hardware";
            Add(Group(s, "Screws", "SCRW", Each(s, "SCRW", o => Pretty(o) + " screw")));
            Add(Group(s, "Shoulder screws", "VXHARDWARE", Vex(s, "VXHARDWARE", o => Contains(o, "Shoulder"))));
            // V2: the original's "Low Profile" nut is VEX's low profile nylock (1/4" hex); "Low Profile Nylock" was a
            // wrong 11/32" copy (kept only so builds that used it still open).
            Add(Group(s, "Nuts", "NUT", Each(s, "NUT", o => o == "Low Profile" ? "Low profile nylock nut (1/4\" hex)" : o + " nut", o => o != "Low Profile Nylock")));
            Add(Group(s, "Standoffs", "SNDF", Each(s, "SNDF", o => Pretty(o) + " standoff")));
            Add(Group(s, "Spacers", "SPCR",
                Group(s, "Nylon (3/8\" OD)", "SPCR", Sizes(s, "SPCR", "Nylon", o => Pretty(o) + " nylon spacer")
                    .Concat(Vex(s, "VXSPACERS", o => Contains(o, "3/8\" OD") && Contains(o, "Nylon") && Contains(o, "7/8\"", "1.00\""), o => o.Replace(" (3/8\" OD)", "")))),
                // V2: no black/white copies of a spacer: any part can be painted any color.
                Group(s, "Nylon (5/16\" OD)", "VXSPACERS", Vex(s, "VXSPACERS", o => Contains(o, "5/16\" OD") && !o.Contains("Black") && !o.Contains("White"), o => o.Replace(" (5/16\" OD)", ""))),
                Group(s, "Nylon (1/2\" OD)", "VXSPACERS", Vex(s, "VXSPACERS", o => Contains(o, "1/2\" OD") && Contains(o, "Nylon") && !o.Contains("Black") && !o.Contains("White"), o => o.Replace(" (1/2\" OD)", ""))),
                Group(s, "High strength", "SPCR", Sizes(s, "SPCR", "High Strength", o => Pretty(o) + " HS spacer")),
                Group(s, "Plastic", "SPCR", Sizes(s, "SPCR", "Plastic", o => Pretty(o) + " plastic spacer")),
                Group(s, "Click-on", "VXSPACERS", Vex(s, "VXSPACERS", o => Contains(o, "Click On")))));
            Add(Group(s, "Washers", "VXSPACERS",
                Group(s, "Steel", "VXSPACERS", Vex(s, "VXSPACERS", o => Contains(o, "Steel Washer"), o => o.Replace(" Steel Washer", ""))),
                Group(s, "Teflon", "VXSPACERS", Vex(s, "VXSPACERS", o => Contains(o, "Teflon Washer"), o => o.Replace(" Teflon Washer", "")))));
            Add(Group(s, "Shaft collars", "CLMP",
                Group(s, "Set screw", "CLMP", Leaf(s, "Shaft collar", "CLMP", "Normal", "Normal")),
                Group(s, "Clamping", "CLMP", Leaf(s, "Clamping shaft collar", "CLMP", "Clamping", "Normal")),
                Group(s, "High strength", "CLMP", Leaf(s, "HS clamping shaft collar", "CLMP", "Clamping", "High Strength")),
                Group(s, "Low profile", "CLMP", Leaf(s, "Low profile HS clamping shaft collar", "CLMP", "Clamping", "Low Profile HS")),
                Group(s, "Rubber", "VXSHAFTPARTS", Vex(s, "VXSHAFTPARTS", o => Contains(o, "Rubber Shaft Collar")))));
            Add(Group(s, "Retainers", "HexNR",
                Group(s, "Nut retainers", "HexNR", new[] { Leaf(s, "Nut retainer", "HexNR", "Nut Retainer") }
                    .Concat(Vex(s, "VXSTRUCTUREPARTS", o => Contains(o, "Hex Nut Retainer")))),
                Group(s, "Standoff retainers", "VXSTRUCTUREPARTS", Vex(s, "VXSTRUCTUREPARTS", o => Contains(o, "Standoff Retainer"))),
                Group(s, "Bearing retainers", "HexNR", new[] { Leaf(s, "Bearing retainer", "HexNR", "Bearing Retainer"), Leaf(s, "Square retainer", "HexNR", "Square Retainer") }
                    .Concat(Vex(s, "VXMOTIONPARTS", o => Contains(o, "1 Post Bearing Flat"))))));
            // V2: couplers and the rubber link (threaded studs) in one family; a lone leaf showed under a bare heading.
            Add(Group(s, "Couplers & rubber link", "VXHARDWARE",
                Group(s, "Coupler", "VXHARDWARE", Vex(s, "VXHARDWARE", o => o == "Coupler")),
                Group(s, "Hex couplers", "VXHARDWARE", Vex(s, "VXHARDWARE", o => Contains(o, "Hex Coupler"))),
                Group(s, "Star couplers", "VXHARDWARE", Vex(s, "VXHARDWARE", o => Contains(o, "Star Coupler"))),
                Group(s, "Rubber link", "VXHARDWARE", Vex(s, "VXHARDWARE", o => o == "Rubber Link"))));
        }

        private static void Structure() {
            const string s = "Structure";
            Add(Group(s, "C-Channels", "CCHL",
                Each(s, "CCHL", o => o + " C-channel").Append(Leaf(s, "2x2 U-channel", "UCHL"))));
            Add(Group(s, "Angles", "ANGL",
                Group(s, "Angles", "ANGL", Each(s, "ANGL", o => o + " angle")),
                Group(s, "Slotted angles", "VXSLOTTEDANGLE", Vex(s, "VXSLOTTEDANGLE")),
                Group(s, "Segmented angle", "VXSEGMENTEDANGLE", Vex(s, "VXSEGMENTEDANGLE", o => !Contains(o, "(25)")))));
            Add(Group(s, "Plates", "PLTE", Leaf(s, "Plate", "PLTE"), Leaf(s, "Base plate", "BSPT")));
            Add(Group(s, "Rails", "RAILS",
                Group(s, "Rail", "RAIL", Leaf(s, "Rail", "RAIL")),
                Group(s, "Rail stock", "RAILS", Each(s, "RAILS", o => o + " rail")),
                Group(s, "Chassis rails", "VXCHASSISRAILS", Vex(s, "VXCHASSISRAILS"))));
            // Every gusset, coupler and plate in one family, by angle (the original's, with their hole behaviour).
            Add(Group(s, "Gussets", "GSET", Options1("GSET").Select(p1 =>
                Group(s, p1 == "Pack" ? "Others" : p1 == "Coupler" ? "Couplers" : p1.Replace(" Degree", "°"), "GSET",
                    Sizes(s, "GSET", p1, o => p1 == "Coupler" || p1 == "Pack" ? o + (p1 == "Coupler" ? " coupler" : " gusset") : p1.Replace(" Degree", "°") + " " + o.ToLowerInvariant())))));
            Add(Group(s, "Brackets", "VXBRACKETS",
                Vex(s, "VXBRACKETS", o => o != "Rack Gearbox Bracket")
                    .Concat(new[] { Leaf(s, "Linear slide bracket", "LMTK", "Linear Slide Bracket") })
                    .Concat(Vex(s, "VXADVANCEDGEARS", o => Contains(o, "Cam Follower Mounting Bracket")))));
            Add(Group(s, "License plates", "VXMISCELLANEOUS", Vex(s, "VXMISCELLANEOUS", o => Contains(o, "License"))));
        }

        // "45 Degree Flat" -> "45° flat gusset", "Plus Gusset" -> "Plus gusset".
        private static string GussetName(string o) {
            var name = Regex.Replace(o, @"(\d+) Degree ", "$1° ");
            name = name.Substring(0, 1) + name.Substring(1).ToLowerInvariant();
            return name.Contains("gusset") || name.Contains("coupler") || name.Contains("corner") ? name : name + " gusset";
        }

        private static void Motion() {
            const string s = "Motion";
            Add(Group(s, "Shafts", "SHFT",
                Leaf(s, "Shaft", "SHFT", "Normal"), Leaf(s, "High strength shaft", "SHFT", "High Strength"),
                Leaf(s, "6\" capped shaft", "VXSHAFTPARTS", "6\" Capped Shaft")));
            Add(Group(s, "Bearings", "BRNG",
                Group(s, "Flat bearings", "BRNG", Each(s, "BRNG", o => o + " bearing flat")),
                Group(s, "Block bearings", "BLCK", Each(s, "BLCK", o => o + " block bearing"))));
            Add(Group(s, "Ball bearings", "VXBALLBEARINGS",
                Vex(s, "VXBALLBEARINGS", o => !Contains(o, "Set Screw", "Collar"))
                    .Concat(Vex(s, "VXHIGHSTRENGTHSH", o => Contains(o, "Bushing")))));
            Add(Wheels());
            Add(FlexWheels());
            Add(Group(s, "Special wheels", "VXWHEELS", Vex(s, "VXWHEELS", o => Contains(o, "Bogie", "Intake Roller", "VersaHub", "VersaHex", "6 Wheel Leg"))));
            Add(Group(s, "Gears", "GEAR",
                Group(s, "Normal", "GEAR", Sizes(s, "GEAR", "Normal", o => o + " gear")),
                Group(s, "High strength", "GEAR", Sizes(s, "GEAR", "High Strength", o => o + " HS gear")),
                Group(s, "High strength v2", "GEAR", Sizes(s, "GEAR", "High Strength v2", o => o + " HS gear v2")),
                Group(s, "Steel", "VXGEARS", Vex(s, "VXGEARS", o => Contains(o, "Steel")))));
            Add(Group(s, "Sprockets", "SPKT",
                Group(s, "Normal", "SPKT", Sizes(s, "SPKT", "Normal", o => o + " sprocket")),
                Group(s, "High strength", "SPKT", Sizes(s, "SPKT", "High Strength", o => o + " HS sprocket")),
                Group(s, "6P (low strength)", "VXSPROCKETS", Vex(s, "VXSPROCKETS", o => Contains(o, "6P"))
                    .Where(n => n != null).OrderBy(n => int.TryParse(Regex.Match(n.name, @"(\d+)T").Groups[1].Value, out var t) ? t : 0)),
                Group(s, "Tank tread drive", "VXSPROCKETS", Vex(s, "VXSPROCKETS", o => Contains(o, "9P"))),
                new CatalogNode { section = s, name = "Make a chain", action = "chain", iconType = "VXCHAIN9P",
                                  tip = "Make a chain or tank tread: click the sprockets it goes round, in order; click one again to wrap it from the other side." }));
            // V2: rubber bands and latex tubing (loops round two or more posts), by who sells them.
            CatalogNode Band(Protobot.Motion.ElasticKind k) => new() {
                section = s, name = k.tubing ? k.name : k.shortName, action = "band:" + k.key, iconType = "SPCR",
                tip = (k.tubing ? "Latex tubing" : "Rubber band") + " " + k.Size + ", " + k.material + " (" + k.source + "). Click the posts it goes round (a standoff, screw, shaft, "
                      + "spacer, a hole or any edge): two clicks make it, Shift+click adds more. It pulls in the simulation as hard as the real one stretched that far." };
            Add(Group(s, "Rubber bands", "SPCR",
                Group(s, "VEX", "SPCR", Protobot.Motion.ElasticKind.All.Where(k => !k.tubing && k.source.StartsWith("VEX")).Select(Band)),
                Group(s, "RoboSource", "SPCR", Protobot.Motion.ElasticKind.All.Where(k => !k.tubing && k.source.StartsWith("RoboSource")).Select(Band))));
            foreach (var k in Protobot.Motion.ElasticKind.All.Where(k => k.tubing)) Add(Band(k));
            Add(Group(s, "Lock bars", "LKBR",
                Group(s, "Normal", "LKBR", Each(s, "LKBR", o => o + " lock bar")),
                Group(s, "High strength", "VXHIGHSTRENGTHSH", Vex(s, "VXHIGHSTRENGTHSH", o => Contains(o, "Lock Bar")))));
            // The Advanced Gear Kit as VEX sells it, by kind; the original's mechanisms (with their behaviour) where
            // VEX-CAD has the same part.
            Add(Group(s, "Advanced Gear Kit", "MCMS",
                Group(s, "Bevel gears", "MCMS", Sizes(s, "MCMS", "Bevel Gear", o => o + " bevel gear")
                    .Concat(Vex(s, "VXADVANCEDGEARS", o => Contains(o, "Bevel Gear") && !Contains(o, "Bracket")))),
                Group(s, "Worm and lead screw", "MCMS", Sizes(s, "MCMS", "Worm and Wheel", o => "Worm " + o.ToLowerInvariant())
                    .Concat(Sizes(s, "MCMS", "Screw Kit", o => "Lead screw " + o.ToLowerInvariant()))
                    .Concat(Vex(s, "VXADVANCEDGEARS", o => o == "Lead Screw Segment"))),
                Group(s, "Cams and cranks", "MCMS", Leaf(s, "Cam follower", "MCMS", "Cam Follower"), Leaf(s, "Drop off cam", "MCMS", "Drop Off Cam"),
                    Leaf(s, "Hand crank", "MCMS", "Hand Crank"), Leaf(s, "Rack", "MCMS", "Rack")),
                Group(s, "Winch and ratchet", "VXADVANCEDGEARS", Vex(s, "VXADVANCEDGEARS", o => Contains(o, "Winch Drum", "Winch Pawl", "Ratchet"))),
                Group(s, "Differential and joints", "VXADVANCEDGEARS", Vex(s, "VXADVANCEDGEARS", o => Contains(o, "Differential", "Universal Joint"))),
                Group(s, "Pulley", "VXMOTIONPARTS", Vex(s, "VXMOTIONPARTS", o => o == "Pulley"))));
            // V2: VEX's linear slide (the truck seats in a slide member and slides along it: Motion/SlideTruck), then
            // the original's slide pieces.
            Add(Group(s, "Linear motion", "LMTK",
                Leaf(s, "Outer slide member", "VXSLIDEASSEMBLYP", "Outer Slide Member"), Leaf(s, "Inner slide member", "VXSLIDEASSEMBLYP", "Inner Slide Member"),
                Leaf(s, "Slide truck", "VXSLIDEASSEMBLYP", "Inner Acetal Slide Truck"),
                Leaf(s, "Inner slide truck (classic)", "LMTK", "Inner Slide Trunk"), Leaf(s, "Outer slide truck (classic)", "LMTK", "Outer Slide Trunk")));
            // V2: one hinge per size (both leaves and the pin: Motion/HingePart), not its three pieces.
            Add(Group(s, "Hinges", "VXHINGES", Vex(s, "VXHINGES", o => o.EndsWith("(bottom)"), o => o.Replace(" Hole Hinge (bottom)", " hole hinge"))));
            Add(Leaf(s, "Rubber bumper", "RBMP"));
        }

        private static CatalogNode Wheels() {
            const string s = "Motion";
            CatalogNode VexWheels(string name, Func<string, bool> filter) => Group(s, name, "VXWHEELS", Vex(s, "VXWHEELS", filter));
            return Group(s, "Wheels", "OMNI",
                Group(s, "Omni wheels", "OMNI", Sizes(s, "OMNI", "V2", o => Pretty(o) + " omni wheel")),
                Group(s, "Traction wheels", "TWHL", Sizes(s, "TWHL", "V2", o => Pretty(o) + " traction wheel")
                    .Concat(Vex(s, "VXWHEELS", o => o == "4\" Traction Tire"))),
                VexWheels("Anti-static omni wheels", o => Contains(o, "Omni") && Contains(o, "Anti Static")),
                VexWheels("Anti-static traction wheels", o => Contains(o, "Traction") && Contains(o, "Anti Static")),
                Group(s, "Mecanum wheels", "MECN",
                    new[] { Leaf(s, "4\" mecanum wheel", "MECN") }.Concat(Vex(s, "VXWHEELS", o => Contains(o, "Mecanum") && o.StartsWith("2\"")))),
                Group(s, "Older wheels (V1)", "OMNI", Sizes(s, "OMNI", "V1", o => Pretty(o) + " omni wheel (V1)")
                    .Concat(Sizes(s, "TWHL", "V1", o => Pretty(o) + " traction wheel (V1)"))));
        }

        // Flex wheels come full width (teams cut them to length with the saw): one per diameter and hardness, plus
        // the original's with/without adapters and the V5 flywheel weight.
        private static CatalogNode FlexWheels() {
            const string s = "Motion";
            var sizes = new[] { "1.625\"", "2\"", "3\"", "4\"" };
            string Widest(string size) => size == "3\"" || size == "4\"" ? "1.00\" wide" : "1/2\" wide";
            return Group(s, "Flex wheels", "FWHL",
                new[] {
                    Group(s, "With adapters", "FWHL", Sizes(s, "FWHL", "With Adapters", o => Pretty(o) + " flex wheel")),
                    Group(s, "No adapters", "FWHL", Sizes(s, "FWHL", "No Adapters", o => Pretty(o) + " flex wheel")),
                }.Concat(sizes.Select(size => Group(s, size + " by hardness", "VXWHEELS",
                    Vex(s, "VXWHEELS", o => o.StartsWith(size + " Flex") && o.EndsWith(Widest(size)), o => o.Replace(", " + Widest(size), " (full width)")))))
                 .Append(Group(s, "Flywheel weight", "VXMOTIONPARTS", Vex(s, "VXMOTIONPARTS", o => o == "V5 Flywheel Weight"))));
        }

        private static void Electronics() {
            const string s = "Electronics";
            // V2: cables, routed from port to port.
            Add(Group(s, "Wiring", "MOTR",
                new CatalogNode { section = s, name = "V5 Smart Cable", action = "wire:V5", iconType = "MOTR",
                                  tip = "Brain to a motor or V5 sensor: click a port, put points where the cable runs, click the port it goes to. Or point at a port and press E." },
                new CatalogNode { section = s, name = "3-wire cable", action = "wire:3W", iconType = "MOTR",
                                  tip = "A brain 3-wire port (A-H) to a sensor, switch or solenoid: click a port, put points where it runs, click where it goes." }));
            Add(Group(s, "Motors", "MOTR", Each(s, "MOTR", o => o + " motor")));
            Add(Group(s, "Brain", "BRAN", new[] { Leaf(s, "Brain", "BRAN") }.Concat(Vex(s, "VXV5ELECTRONICS", o => Contains(o, "Brain Mount")))));
            Add(Leaf(s, "Battery", "BTRY"));
            Add(Leaf(s, "Radio", "RDIO"));
            Add(Leaf(s, "Battery clip", "BTCL"));
            Add(Group(s, "Sensors", "SNSR",
                Group(s, "V5 sensors", "SNSR", Options1("SNSR").Where(o => Options2("SNSR", o).Contains("V5")).Select(o => Leaf(s, o + " sensor", "SNSR", o, "V5"))
                    .Concat(Vex(s, "VXSENSORS", o => Contains(o, "GPS Sensor", "Potentiometer V2")))),
                Group(s, "3-wire (legacy) sensors", "VXSENSORS",
                    Options1("SNSR").Where(o => Options2("SNSR", o).Contains("Cortex")).Select(o => Leaf(s, o + " sensor (3-wire)", "SNSR", o, "Cortex"))
                        .Concat(Vex(s, "VXSENSORS", o => Contains(o, "Accelerometer", "Light Sensor", "Line Tracker V2", "Line Sensor Mounting Bar", "Optical Shaft Encoder",
                            "Ultrasonic", "Yaw Rate"))))));
            Add(Group(s, "Switches", "SWCH",
                Group(s, "Bumper switches", "SWCH", Leaf(s, "Bumper switch (V5)", "SWCH", "Bumper", "V5"), Leaf(s, "Bumper switch (3-wire)", "SWCH", "Bumper", "Cortex")),
                Group(s, "Limit switches", "SWCH", Leaf(s, "Limit switch", "SWCH", "Limit", "Cortex"))));
            Add(Group(s, "3-wire expander", "VXSENSORS", Vex(s, "VXSENSORS", o => Contains(o, "3 Wire Expander"))));
        }

        private static void Pneumatics() {
            const string s = "Pneumatics";
            // V2: one cylinder per stroke: it's placed retracted and extends (right-click), so the original's
            // separate "extended" models (still loaded from old builds) and VEX's body and rod aren't listed.
            Add(Group(s, "Cylinders", "PNMT",
                Options1("PNMT").Select(size => size.StartsWith("Legacy")
                    ? Leaf(s, size.Replace("Legacy ", "") + " cylinder (legacy)", "PNMT", size, "Normal", "VEX's older (SMC) " + size.Replace("Legacy ", "").ToLowerInvariant() + " cylinder, 2\" stroke" + (size.Contains("Single") ? ": air pushes it out, a spring brings it back" : "") + ". Right-click it to extend or retract")
                    : Leaf(s, size + " cylinder", "PNMT", size, "Normal", "V5 pneumatic cylinder, " + size + " stroke: right-click it to extend or retract"))));
            Add(Group(s, "Cylinder mounts", "VXPNEUMATICS", Vex(s, "VXPNEUMATICS", o => Contains(o, "Cylinder Mount", "Cylinder Rod Pivot", "Piston Nut"))));
            Add(Group(s, "Reservoirs", "TANK", Leaf(s, "V5 reservoir", "TANK", "V5"), Leaf(s, "Legacy reservoir", "TANK", "Legacy")));
            Add(Group(s, "Fittings", "VXPNEUMATICS",
                Group(s, "Part fittings", "VXPNEUMATICS", Vex(s, "VXPNEUMATICS", o => Contains(o, "Fitting for"))),
                Group(s, "Tubing fittings", "VXPNEUMATICS", Vex(s, "VXPNEUMATICS", o => Contains(o, "T Fitting", "Tee Fittings", "Elbow Fitting", "Straight Pneumatic Fitting", "Fitting Plug", "Air Flow Valve Fitting"))),
                Group(s, "Valves", "VXPNEUMATICS", Vex(s, "VXPNEUMATICS", o => Contains(o, "Schrader Valve", "Hand Valve")))));
            // V2: tubing, routed from fitting to fitting.
            Add(new CatalogNode { section = s, name = "Tubing (4 mm)", action = "ptube", iconType = "VXPNEUMATICS",
                                  tip = "V5 pneumatic tubing: start at a fitting's end, click to put points where it runs (through holes, round corners), click the fitting it goes to. Or right-click a fitting > Run tubing from here." });
            Add(Group(s, "Solenoids and controls", "VXPNEUMATICS",
                Vex(s, "VXPNEUMATICS", o => Contains(o, "Solenoid", "Manifold", "Regulator", "Flow Meter", "On Off Switch"))));
        }

        private static void Field() {
            const string s = "Field";
            Add(Group(s, "Override (2026-27)", "OVFL",
                Group(s, "Game elements", "OVPN", Each(s, "OVPN", o => o + " pin").Append(Leaf(s, "Cup", "OVCP"))),
                // V2: OVSG/OVTG/OVAG are the same models as OVGL's (kept for old builds, not listed twice).
                Group(s, "Goals", "OVGL", Each(s, "OVGL", o => o + " goal")),
                Group(s, "Field parts", "OVFL", Each(s, "OVFL", o => o + " field").Concat(Each(s, "OVLD", o => o + " loader")).Append(Leaf(s, "Toggle", "OVTO")))));
            Add(Group(s, "Push Back (2025-26)", "PUBA",
                Group(s, "Game elements", "BLOK", Each(s, "BLOK", o => o + " block")),
                Group(s, "Field parts", "PUBA", Leaf(s, "Push Back field", "PUBA"), Leaf(s, "Long goal", "LGOL"), Leaf(s, "Center goal", "CGOL"), Leaf(s, "Loader", "LOAD"))));
            Add(Group(s, "High Stakes (2024-25)", "FELD",
                Group(s, "Game elements", "RING", Each(s, "RING", o => o + " ring")),
                Group(s, "Field parts", "FELD", Each(s, "FELD", o => o + " field").Append(Leaf(s, "Stake", "SAKE")))));
            Add(Group(s, "Spin Up (2022-23)", "DISC", Leaf(s, "Disc", "DISC")));
        }

        private static void Custom() {
            const string s = "Custom";
            nodes.Add(new CatalogNode { section = s, name = "Import part", action = "import", tip = "Add your own part from a 3D model file (STL, OBJ, 3MF, GLB, glTF, PLY, OFF)." });
            nodes.Add(new CatalogNode { section = s, name = "Polycarbonate", action = "polycarb", tip = "Sketch a polycarbonate piece on the sheet and cut it out." });
            nodes.Add(new CatalogNode { section = s, name = "Library", action = "library", tip = "Your imported parts and plastic pieces: edit them, or delete ones you don't need." });
            // V2: saved groups (right-click a part > Save group…).
            var groups = Builds.SavedGroups.Names();
            if (groups.Count > 0)
                nodes.Add(new CatalogNode { section = s, name = "Saved groups", children = groups.Select(g => new CatalogNode { section = s, name = g, action = "group:" + g, tip = "A saved group of parts: placed in the middle of the view" }).ToList() });
            foreach (var part in Protobot.Custom.CustomParts.All.OrderByDescending(p => p.created))
                nodes.Add(new CatalogNode { section = s, name = part.name, customId = part.Id, typeId = part.kind, p1 = part.key,
                    tip = part.kind == Protobot.Custom.CustomParts.Polycarbonate ? "Polycarbonate piece" : "Imported from " + part.source });
        }
    }
}
