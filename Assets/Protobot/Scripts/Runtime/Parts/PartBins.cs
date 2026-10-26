using System.Collections.Generic;

namespace Protobot.Parts
{
    /// <summary>
    /// The 24 bins on the workshop shelf (4 rows x 6). Each bin holds a category of parts;
    /// clicking a bin opens a picker with every part in it (sizes, variants, search).
    /// Row colours match the bins in the model: blue = structure, red = motion,
    /// yellow = hardware & wheels, green = electronics & other.
    /// </summary>
    public static class PartBins
    {
        public const string Recent = "recent";

        public readonly struct Bin
        {
            public readonly string id;
            public readonly string label;

            public Bin(string id, string label)
            {
                this.id = id;
                this.label = label;
            }
        }

        /// <summary>Index = bin number on the shelf (Bin_00 is bottom row, nearest the back wall).</summary>
        public static readonly IReadOnlyList<Bin> All = new[]
        {
            new Bin("c_channels", "C-Channels"), new Bin("angles", "Angles"), new Bin("plates", "Plates"),
            new Bin("rails", "Rails & U-Channels"), new Bin("gussets", "Gussets & Brackets"), new Bin("standoffs", "Standoffs"),

            new Bin("shafts", "Shafts"), new Bin("gears", "Gears"), new Bin("sprockets", "Sprockets & Chain"),
            new Bin("bearings", "Bearings & Collars"), new Bin("spacers", "Spacers & Washers"), new Bin("advanced", "Advanced Motion"),

            new Bin("screws", "Screws"), new Bin("nuts", "Nuts & Retainers"), new Bin("omni_wheels", "Omni Wheels"),
            new Bin("wheels", "Other Wheels"), new Bin("hinges", "Hinges & Slides"), new Bin("pneumatics", "Pneumatics"),

            new Bin("motors", "Motors"), new Bin("brain", "Brain & Power"), new Bin("sensors", "Sensors"),
            new Bin("game", "Game Elements"), new Bin("misc", "Miscellaneous"), new Bin(Recent, "Recently Used"),
        };

        /// <summary>Which bin a part belongs in, from its path in the VEX-CAD library.</summary>
        public static string ForLibraryPath(string path)
        {
            var p = path.Replace('\\', '/').ToLowerInvariant();
            var name = p.Substring(p.LastIndexOf('/') + 1);

            if (p.StartsWith("game objects")) return "game";
            if (p.StartsWith("miscellaneous")) return "misc";

            if (p.StartsWith("electronics"))
            {
                if (name.Contains("motor")) return "motors";
                if (p.Contains("/sensors/") || name.Contains("sensor") || name.Contains("encoder")) return "sensors";
                return "brain";
            }

            if (p.StartsWith("structure"))
            {
                if (p.Contains("c channel")) return name.Contains("2x2x2") ? "rails" : "c_channels";
                if (p.Contains("angle")) return "angles";
                if (p.Contains("/plate")) return "plates";
                if (p.Contains("chassis rail")) return "rails";
                if (p.Contains("gusset") || p.Contains("bracket")) return "gussets";
                if (p.Contains("standoff") || name.Contains("standoff") || name.Contains("coupler")) return "standoffs";
                if (name.Contains("screw") || p.Contains("screw")) return "screws";
                if (name.Contains("nut") || name.Contains("retainer") || p.Contains("nylon hardware")) return "nuts";
                return "misc";
            }

            if (p.StartsWith("motion"))
            {
                if (p.Contains("pneumatic")) return "pneumatics";
                if (p.Contains("hinge") || p.Contains("slide")) return "hinges";
                if (p.Contains("/wheels/")) return name.Contains("omni") ? "omni_wheels" : "wheels";
                if (p.Contains("sprocket") || p.Contains("chain") || name.Contains("drive wheel")) return "sprockets";
                if (p.Contains("spacer") || name.Contains("washer") || name.Contains("bushing")) return "spacers";
                if (name.Contains("collar") || name.Contains("bearing") || name.Contains("lock bar") || name.Contains("insert")
                    || name.Contains("retainer") || name.Contains("adapter")) return "bearings";
                if (p.Contains("shaft")) return "shafts";
                if (name.Contains("gear") && !p.Contains("advanced")) return "gears";
                if (p.Contains("/gears")) return "gears";
                return "advanced";
            }

            return "misc";
        }
    }
}
