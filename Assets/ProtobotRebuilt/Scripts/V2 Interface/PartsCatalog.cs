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
        public string action;           // "import", "polycarb", "sheets"
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
                if (used.Contains(type.id) || type.id == Protobot.Custom.CustomParts.Imported || type.id == Protobot.Custom.CustomParts.Polycarbonate) continue;
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

        private static bool IsVex(string id) => id.StartsWith("VX");
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

        private static void Hardware() {
            const string s = "Hardware";
            Add(Group(s, "Screws", "SCRW", Each(s, "SCRW", o => Pretty(o) + " screw")));
            Add(Group(s, "Nuts", "NUT", Each(s, "NUT", o => o + " nut")));
            Add(Group(s, "Standoffs", "SNDF", Each(s, "SNDF", o => Pretty(o) + " standoff")
                .Concat(new[] { Family(s, "VEX standoff models", "VXSTANDOFFVEX") })));
            Add(Group(s, "Spacers", "SPCR",
                Group(s, "Nylon", "SPCR", Sizes(s, "SPCR", "Nylon", o => Pretty(o) + " nylon spacer")),
                Group(s, "Plastic", "SPCR", Sizes(s, "SPCR", "Plastic", o => Pretty(o) + " plastic spacer")),
                Group(s, "High strength", "SPCR", Sizes(s, "SPCR", "High Strength", o => Pretty(o) + " HS spacer")),
                Family(s, "5/16\" OD nylon", "VXSPACERS", o => Contains(o, "5/16\" OD")),
                Family(s, "3/8\" OD nylon", "VXSPACERS", o => Contains(o, "3/8\" OD") && Contains(o, "Nylon")),
                Family(s, "1/2\" OD nylon", "VXSPACERS", o => Contains(o, "1/2\" OD") && Contains(o, "Nylon")),
                Family(s, "Click-on", "VXSPACERS", o => Contains(o, "Click On")),
                Family(s, "Plastic (VEX)", "VXSPACERS", o => Contains(o, "Plastic Spacer", "Spacer Plastic"))));
            Add(Group(s, "Washers", "WSHR",
                Leaf(s, "Steel washer", "WSHR", "Steel"),
                Leaf(s, "Teflon washer", "WSHR", "Teflon"),
                Family(s, "VEX washer models", "VXSPACERS", o => Contains(o, "Washer"))));
            Add(Group(s, "Shaft collars", "CLMP",
                Leaf(s, "Shaft collar", "CLMP", "Normal"),
                Leaf(s, "Clamping shaft collar", "CLMP", "Clamping"),
                Family(s, "VEX collar models", "VXSHAFTPARTS", o => Contains(o, "Collar")),
                Family(s, "High strength collars", "VXHIGHSTRENGTHSH", o => Contains(o, "Collar"))));
            Add(Group(s, "Couplers & links", "VXHARDWARE",
                Each(s, "VXHARDWARE", o => o, o => !Contains(o, "Screw", "Pin"))));
            Add(Family(s, "Shoulder screws", "VXHARDWARE", o => Contains(o, "Shoulder") && !Contains(o, "threads shown")));
        }

        private static void Structure() {
            const string s = "Structure";
            Add(Group(s, "C-Channels", "CCHL",
                Each(s, "CCHL", o => o + " C-channel").Append(Leaf(s, "U-Channel", "UCHL"))
                    .Append(Family(s, "Full-length models", "VXCCHANNELSTOCK"))));
            Add(Group(s, "Angles", "ANGL",
                Each(s, "ANGL", o => o + " angle")
                    .Append(Family(s, "Full-length models", "VXANGLESTOCK"))
                    .Append(Family(s, "Slotted angle", "VXSLOTTEDANGLE"))
                    .Append(Family(s, "Segmented angle", "VXSEGMENTEDANGLE"))));
            Add(Group(s, "Plates", "PLTE",
                Leaf(s, "Plate", "PLTE"), Leaf(s, "Base plate", "BSPT"), Family(s, "Full-length models", "VXPLATESTOCK")));
            Add(Group(s, "Rails", "RAILS",
                Leaf(s, "Rail", "RAIL"), Group(s, "Rail stock", "RAILS", Each(s, "RAILS", o => o + " rail")),
                Family(s, "Chassis rails", "VXCHASSISRAILS")));
            Add(Group(s, "Gussets", "GSET",
                Each(s, "GSET", o => o + " gusset").Append(Family(s, "VEX gusset models", "VXGUSSETSVEX"))));
            Add(Family(s, "Brackets", "VXBRACKETS"));
            Add(Group(s, "Retainers", "HexNR",
                Each(s, "HexNR", o => o).Concat(Each(s, "VXSTRUCTUREPARTS", o => o, o => Contains(o, "Retainer")))));
            Add(Family(s, "License plates", "VXMISCELLANEOUS"));
        }

        private static void Motion() {
            const string s = "Motion";
            Add(Group(s, "Shafts", "SHFT",
                Leaf(s, "Shaft", "SHFT", "Normal"), Leaf(s, "High strength shaft", "SHFT", "High Strength"),
                Family(s, "VEX shaft models", "VXSHAFTPARTS", o => Contains(o, "Shaft") && !Contains(o, "Collar"))));
            Add(Group(s, "Bearings", "BRNG",
                Group(s, "Flat bearings", "BRNG", Each(s, "BRNG", o => o + " bearing flat")),
                Group(s, "Block bearings", "BLCK", Each(s, "BLCK", o => o + " block bearing")),
                Family(s, "Ball bearings", "VXBALLBEARINGS")));
            Add(Wheels());
            Add(Group(s, "Gears", "GEAR",
                Group(s, "Normal", "GEAR", Sizes(s, "GEAR", "Normal", o => o + " gear")),
                Group(s, "High strength", "GEAR", Sizes(s, "GEAR", "High Strength", o => o + " HS gear")),
                Group(s, "High strength v2", "GEAR", Sizes(s, "GEAR", "High Strength v2", o => o + " HS gear v2")),
                Family(s, "VEX gear models", "VXGEARS")));
            Add(Group(s, "Sprockets & chain", "SPKT",
                Group(s, "Normal sprockets", "SPKT", Sizes(s, "SPKT", "Normal", o => o + " sprocket")),
                Group(s, "High strength sprockets", "SPKT", Sizes(s, "SPKT", "High Strength", o => o + " HS sprocket")),
                Family(s, "VEX sprocket models", "VXSPROCKETS"),
                Family(s, "Chain & tank tread", "VXCHAIN9P")));
            Add(Group(s, "Lock bars", "LKBR",
                Each(s, "LKBR", o => o + " lock bar").Concat(Each(s, "VXHIGHSTRENGTHSH", o => o, o => Contains(o, "Lock Bar")))));
            // The Advanced Gear Kit stays together, as VEX sells it.
            Add(Family(s, "Advanced Gear Kit", "VXADVANCEDGEARS"));
            Add(Group(s, "Mechanisms", "MCMS", Each(s, "MCMS")));
            Add(Group(s, "Linear motion", "LMTK", Each(s, "LMTK").Append(Family(s, "Slide assembly", "VXSLIDEASSEMBLYP"))));
            Add(Group(s, "Hinges", "VXHINGES",
                Family(s, "2 hole", "VXHINGES", o => o.StartsWith("2 ")), Family(s, "3 hole", "VXHINGES", o => o.StartsWith("3 ")),
                Family(s, "4 hole", "VXHINGES", o => o.StartsWith("4 ")), Family(s, "5 hole", "VXHINGES", o => o.StartsWith("5 "))));
            Add(Leaf(s, "Rubber bumper", "RBMP"));
            Add(Group(s, "Motion parts", "VXMOTIONPARTS",
                Each(s, "VXMOTIONPARTS", o => o)
                    .Concat(Each(s, "VXHIGHSTRENGTHSH", o => o, o => !Contains(o, "Collar", "Lock Bar")))));
        }

        private static CatalogNode Wheels() {
            const string s = "Motion";
            var vex = Options1("VXWHEELS");
            CatalogNode VexWheels(string name, Func<string, bool> filter) => Family(s, name, "VXWHEELS", filter);
            var flexSizes = new[] { "1.625\"", "2\"", "3\"", "4\"" };
            return Group(s, "Wheels", "OMNI",
                Group(s, "Omni wheels", "OMNI",
                    Sizes(s, "OMNI", "V2", o => Pretty(o) + " omni wheel")
                        .Append(Group(s, "Older (V1)", "OMNI", Sizes(s, "OMNI", "V1", o => Pretty(o) + " omni wheel (V1)")))
                        .Append(VexWheels("Anti-static", o => Contains(o, "Omni") && Contains(o, "Anti Static")))
                        .Append(VexWheels("Other omni models", o => Contains(o, "Omni") && !Contains(o, "Anti Static")))),
                Group(s, "Traction wheels", "TWHL",
                    Sizes(s, "TWHL", "V2", o => Pretty(o) + " traction wheel")
                        .Append(Group(s, "Older (V1)", "TWHL", Sizes(s, "TWHL", "V1", o => Pretty(o) + " traction wheel (V1)")))
                        .Append(VexWheels("Anti-static", o => Contains(o, "Traction") && Contains(o, "Anti Static")))
                        .Append(VexWheels("Other traction models", o => Contains(o, "Traction") && !Contains(o, "Anti Static")))),
                Group(s, "Mecanum wheels", "MECN",
                    new[] { Leaf(s, "Mecanum wheel", "MECN") }.Concat(Each(s, "VXWHEELS", o => o, o => Contains(o, "Mecanum")))),
                Group(s, "Flex wheels", "FWHL",
                    new[] {
                        Group(s, "With adapters", "FWHL", Sizes(s, "FWHL", "With Adapters", o => Pretty(o) + " flex wheel")),
                        Group(s, "No adapters", "FWHL", Sizes(s, "FWHL", "No Adapters", o => Pretty(o) + " flex wheel")),
                    }.Concat(flexSizes.Select(size => VexWheels(size + " (by hardness)", o => o.StartsWith(size + " Flex"))))),
                VexWheels("Other wheels", o => !Contains(o, "Omni", "Traction", "Mecanum", "Flex Wheel")));
        }

        private static void Electronics() {
            const string s = "Electronics";
            Add(Group(s, "Motors", "MOTR", Each(s, "MOTR", o => o + " motor")));
            Add(Leaf(s, "Brain", "BRAN"));
            Add(Leaf(s, "Battery", "BTRY"));
            Add(Leaf(s, "Radio", "RDIO"));
            Add(Leaf(s, "Battery clip", "BTCL"));
            Add(Group(s, "Sensors", "SNSR",
                Each(s, "SNSR", o => o + " sensor").Append(Family(s, "VEX sensor models", "VXSENSORS"))));
            Add(Group(s, "Switches", "SWCH",
                Group(s, "V5", "SWCH", Options1("SWCH").Select(o => Leaf(s, o + " switch", "SWCH", o, "V5"))),
                Group(s, "Cortex", "SWCH", Options1("SWCH").Select(o => Leaf(s, o + " switch (Cortex)", "SWCH", o, "Cortex")))));
            Add(Family(s, "V5 electronics models", "VXV5ELECTRONICS"));
        }

        private static void Pneumatics() {
            const string s = "Pneumatics";
            Add(Group(s, "Cylinders", "PNMT",
                Options1("PNMT").Select(size => Group(s, size, "PNMT", Sizes(s, "PNMT", size, o => size + " " + o.ToLowerInvariant() + " cylinder")))));
            Add(Group(s, "Reservoirs", "TANK", Each(s, "TANK", o => o + " reservoir")));
            // The pneumatics kit, kept together by kind.
            Add(Group(s, "Pneumatics kit", "VXPNEUMATICS",
                Family(s, "Pistons", "VXPNEUMATICS", o => Contains(o, "Piston") && !Contains(o, "Nut")),
                Family(s, "Fittings & tubing", "VXPNEUMATICS", o => Contains(o, "Fitting", "Tee", "Plug")),
                Family(s, "Valves & solenoids", "VXPNEUMATICS", o => Contains(o, "Valve", "Solenoid", "Switch", "Manifold", "Regulator", "Flow")),
                Family(s, "Mounts & other", "VXPNEUMATICS", o => !Contains(o, "Piston", "Fitting", "Tee", "Plug", "Valve", "Solenoid", "Switch", "Manifold", "Regulator", "Flow") || Contains(o, "Piston Nut"))));
        }

        private static void Field() {
            const string s = "Field";
            Add(Group(s, "Override (2026-27)", "OVFL",
                Group(s, "Field", "OVFL", Each(s, "OVFL", o => o + " field")),
                Group(s, "Goals", "OVGL", Each(s, "OVGL", o => o + " goal")
                    .Concat(new[] { Leaf(s, "Short goal", "OVSG"), Leaf(s, "Tall goal", "OVTG") })
                    .Concat(Each(s, "OVAG", o => o + " alliance goal"))),
                Group(s, "Pins", "OVPN", Each(s, "OVPN", o => o + " pin")),
                Group(s, "Loaders", "OVLD", Each(s, "OVLD", o => o + " loader")),
                Leaf(s, "Cup", "OVCP"), Leaf(s, "Toggle", "OVTO")));
            Add(Group(s, "Push Back (2025-26)", "PUBA",
                Leaf(s, "Push Back field", "PUBA"), Group(s, "Blocks", "BLOK", Each(s, "BLOK", o => o + " block")),
                Leaf(s, "Long goal", "LGOL"), Leaf(s, "Center goal", "CGOL"), Leaf(s, "Loader", "LOAD")));
            Add(Group(s, "High Stakes (2024-25)", "FELD",
                Group(s, "Field", "FELD", Each(s, "FELD", o => o + " field")), Group(s, "Rings", "RING", Each(s, "RING", o => o + " ring")),
                Leaf(s, "Stake", "SAKE")));
            Add(Group(s, "Spin Up (2022-23)", "DISC", Leaf(s, "Disc", "DISC")));
        }

        private static void Custom() {
            const string s = "Custom";
            nodes.Add(new CatalogNode { section = s, name = "Import part", action = "import", tip = "Add your own part from a 3D model file (STL, OBJ, 3MF, GLB, glTF, PLY, OFF)." });
            nodes.Add(new CatalogNode { section = s, name = "Polycarbonate", action = "polycarb", tip = "Sketch a polycarbonate piece on the sheet and cut it out." });
            foreach (var part in Protobot.Custom.CustomParts.All.OrderByDescending(p => p.created))
                nodes.Add(new CatalogNode { section = s, name = part.name, customId = part.Id, typeId = part.kind, p1 = part.key,
                    tip = part.kind == Protobot.Custom.CustomParts.Polycarbonate ? "Polycarbonate piece" : "Imported from " + part.source });
        }
    }
}
