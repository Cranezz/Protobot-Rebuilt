using System.Globalization;
using System.Text.RegularExpressions;
using Protobot.Core;

namespace Protobot.Editor
{
    /// <summary>
    /// Turns VEX-CAD library file names ("Alu C 1x2x1x35", "SScrew Star Smooth .250", "NSpacer .313 OD x .125")
    /// into the names builders use, in the style of the original Protobot Rebuilt: size first with an inch mark
    /// (fractions below an inch, two decimals above: 1/4", 2.50"), then the kind of part — "1x2x1 Aluminum
    /// C-Channel (35)", "1/4" Star Screw", "1/8" Nylon Spacer (5/16" OD)", "Normal 36T Gear", "11W Motor".
    /// Anything without a rule keeps its library name.
    /// </summary>
    public static class PartNaming
    {
        public static string DisplayName(string libraryPath)
        {
            var slash = libraryPath.LastIndexOf('/');
            var name = libraryPath.Substring(slash + 1).Trim();
            var folder = slash > 0 ? libraryPath.Substring(0, slash) : "";
            return Regex.Replace(Rename(name, folder), @"\s+", " ").Trim();
        }

        static string Rename(string n, string folder)
        {
            Match m;

            // ---- structure: channels, angles, rails, plates ("1x2x1 Aluminum C-Channel (35)")
            if ((m = Regex.Match(n, @"^(Alu|Steel) (Half )?C(?: Chan)?(?: (Half))?(?: (Thick))? (\d+x\d+x\d+)x(\d+)$")).Success)
            {
                var half = m.Groups[2].Success || m.Groups[3].Success ? "Half " : "";
                var thick = m.Groups[4].Success ? ", Thick Cut" : "";
                return $"{m.Groups[5].Value} {Metal(m.Groups[1].Value)} {half}C-Channel{thick} ({m.Groups[6].Value})";
            }
            if ((m = Regex.Match(n, @"^(Alu|Steel) +Angle (\d+x\d+)x(\d+)$")).Success)
                return $"{m.Groups[2].Value} {Metal(m.Groups[1].Value)} Angle ({m.Groups[3].Value})";
            if ((m = Regex.Match(n, @"^Segmented Angle (\d+x\d+)x(\d+)$")).Success)
                return $"{m.Groups[1].Value} Segmented Angle ({m.Groups[2].Value})";
            if ((m = Regex.Match(n, @"^(Alu|Steel) Chassis Rail (\d+x\d+)x(\d+)$")).Success)
                return $"{m.Groups[2].Value} {Metal(m.Groups[1].Value)} Chassis Rail ({m.Groups[3].Value})";
            if ((m = Regex.Match(n, @"^(Alu|Steel) Plate (\d+x\d+)$")).Success)
                return $"{Metal(m.Groups[1].Value)} Plate ({m.Groups[2].Value})";
            if (n == "C Channel Coupler")
                return "C-Channel Coupler";
            if ((m = Regex.Match(n, @"^C Channel (.+)$")).Success)
                return $"{m.Groups[1].Value} C-Channel";
            if ((m = Regex.Match(n, @"^Angle (\d+x\d+)$")).Success)
                return $"{m.Groups[1].Value} Angle";

            // ---- gussets ("90 Degree Bent")
            if ((m = Regex.Match(n, @"^(\d+) (Bent|Flat|Plate|Angle) Gusset$")).Success)
                return $"{m.Groups[1].Value} Degree {m.Groups[2].Value}";
            if ((m = Regex.Match(n, @"^(\d+) Old Gusset$")).Success)
                return $"{m.Groups[1].Value} Degree Gusset (old)";
            if ((m = Regex.Match(n, @"^Angle (Corner|Coupler) Gusset$")).Success)
                return $"Angle {m.Groups[1].Value}";

            // ---- shafts ("24" HS Shaft")
            if ((m = Regex.Match(n, @"^Shaft HS ([\d.]+)(?: in)?$")).Success)
                return $"{Inch(m.Groups[1].Value, true)} HS Shaft";
            if ((m = Regex.Match(n, @"^Shaft LS Capped ([\d.]+)$")).Success)
                return $"{Inch(m.Groups[1].Value, true)} Capped Shaft";
            if ((m = Regex.Match(n, @"^Shaft LS ([\d.]+)$")).Success)
                return $"{Inch(m.Groups[1].Value, true)} Shaft";

            // ---- standoffs, couplers, screws ("1/2" Standoff", "1/4" Star Screw")
            if ((m = Regex.Match(n, @"^Standoff ([\d.]+)$")).Success)
                return $"{Inch(m.Groups[1].Value)} Standoff";
            if ((m = Regex.Match(n, @"^Coupler (Hex|Star) ([\d.]+)$")).Success)
                return $"{Inch(m.Groups[2].Value)} {m.Groups[1].Value} Coupler";
            if ((m = Regex.Match(n, @"^SScrew (Hex|Star) (Smooth|Threaded) ([\d.]+)$")).Success)
                return $"{Inch(m.Groups[3].Value)} {m.Groups[1].Value} Screw{(m.Groups[2].Value == "Threaded" ? ", threads shown" : "")}";
            if ((m = Regex.Match(n, @"^SScrew Shoulder (Smooth|Threaded) ([\d.]+) x ([\d.]+)$")).Success)
                return $"{Inch(m.Groups[2].Value)} Shoulder Screw ({Inch(m.Groups[3].Value)} shoulder){(m.Groups[1].Value == "Threaded" ? ", threads shown" : "")}";
            if ((m = Regex.Match(n, @"^Screw Steel - (Hex|Star)$")).Success)
                return $"{m.Groups[1].Value} Screw";

            // ---- spacers and washers ("1/8" Nylon Spacer (5/16" OD)", "4.6mm Plastic Spacer")
            if ((m = Regex.Match(n, @"^HSpacer ([\d.]+)$")).Success)
                return $"{Inch(m.Groups[1].Value)} HS Spacer";
            if ((m = Regex.Match(n, @"^HS Bushing ([\d.]+)$")).Success)
                return $"{Inch(m.Groups[1].Value)} HS Bushing";
            if ((m = Regex.Match(n, @"^NSpacer (?:(Black|White) )?([\d.]+) OD x ([\d.]+)$")).Success)
                return $"{Inch(m.Groups[3].Value)} {(m.Groups[1].Success ? m.Groups[1].Value + " " : "")}Nylon Spacer ({Inch(m.Groups[2].Value)} OD)";
            if ((m = Regex.Match(n, @"^Nspacer Click On x ([\d.]+)$")).Success)
                return $"{Inch(m.Groups[1].Value)} Click-On Spacer";
            if ((m = Regex.Match(n, @"^PSpacer ([\d.]+)$")).Success)
                return $"{float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture):0.#}mm Plastic Spacer";
            if ((m = Regex.Match(n, @"^([ST])Washer ([\d.]+) OD x ([\d.]+)$")).Success)
                return $"{Inch(m.Groups[3].Value)} {(m.Groups[1].Value == "S" ? "Steel" : "Teflon")} Washer ({Inch(m.Groups[2].Value)} OD)";
            if ((m = Regex.Match(n, @"^Spacer Nylon ([\d.]+) OD$")).Success)
                return $"Nylon Spacer ({Inch(m.Groups[1].Value)} OD)";

            // ---- gears and sprockets ("Normal 36T Gear", "HS 12T Gear", "HS 18T Sprocket")
            if ((m = Regex.Match(n, @"^LS (\d+T) Gear( Steel)?$")).Success)
                return $"Normal {m.Groups[1].Value}{(m.Groups[2].Success ? " Steel" : "")} Gear";
            if ((m = Regex.Match(n, @"^HS (\d+T) Gear(?:, No Insert)?$")).Success)
                return $"HS {m.Groups[1].Value} Gear";
            if ((m = Regex.Match(n, @"^3P (\d+T) Sprocket$")).Success)
                return $"Normal {m.Groups[1].Value} Sprocket";
            if ((m = Regex.Match(n, @"^9P (\d+T) Sprocket(?:, No Insert)?$")).Success)
                return $"HS {m.Groups[1].Value} Sprocket";
            if ((m = Regex.Match(n, @"^6P (\d+T) Sprocket$")).Success)
                return $"6P {m.Groups[1].Value} Sprocket";

            // ---- wheels ("2.75" Omni Wheel", "3" Flex Wheel, 40A, 1/2" wide")
            if ((m = Regex.Match(n, @"^([\d.]+) Omni(?: (Antistatic|AS|Half|Old|Locked))?(?:, No Insert)?$")).Success)
            {
                var kind = m.Groups[2].Value;
                return kind is "Antistatic" or "AS" ? $"{Inch(m.Groups[1].Value, true)} Anti-Static Omni Wheel"
                    : kind.Length > 0 ? $"{Inch(m.Groups[1].Value, true)} Omni Wheel ({kind.ToLowerInvariant()})"
                    : $"{Inch(m.Groups[1].Value, true)} Omni Wheel";
            }
            if ((m = Regex.Match(n, @"^([\d.]+) Traction(?: (Antistatic|Tire|\d+))?$")).Success)
            {
                var kind = m.Groups[2].Value;
                return kind == "Antistatic" ? $"{Inch(m.Groups[1].Value, true)} Anti-Static Traction Wheel"
                    : kind == "Tire" ? $"{Inch(m.Groups[1].Value, true)} Traction Tire"
                    : kind.Length > 0 ? $"{Inch(m.Groups[1].Value, true)} Traction Wheel ({kind})"
                    : $"{Inch(m.Groups[1].Value, true)} Traction Wheel";
            }
            if ((m = Regex.Match(n, @"^([\d.]+) Mecanum (Left|Right)$")).Success)
                return $"{Inch(m.Groups[1].Value, true)} Mecanum Wheel ({m.Groups[2].Value.ToLowerInvariant()})";
            if ((m = Regex.Match(n, @"^([\d.]+) Flex Wheel$")).Success)
                return $"{Inch(m.Groups[1].Value, true)} Flex Wheel";
            if ((m = Regex.Match(n, @"^([\d.]+) (\d+A) ([\d.]+)$")).Success && folder.Contains("Flex Wheel"))
                return $"{Inch(m.Groups[1].Value, true)} Flex Wheel, {m.Groups[2].Value}, {Inch(m.Groups[3].Value)} wide";
            if ((m = Regex.Match(n, @"^VersaHex Adapters ([\d.]+)$")).Success)
                return $"{Inch(m.Groups[1].Value)} VersaHex Adapter";

            // ---- electronics ("11W Motor", "Brain", "V5 Bumper Switch")
            switch (n)
            {
                case "V5 Motor": return "11W Motor";
                case "V5 Motor (5.5W)": return "5.5W Motor";
                case "V5 Simple Motor": return "11W Motor (simple)";
                case "V5 Brain": return "Brain";
                case "V5 Simple Brain": return "Brain (simple)";
                case "V5 Radio": return "Radio";
                case "V5 Simple Radio": return "Radio (simple)";
                case "V5 Battery": return "Battery";
                case "V5 Simple Battery": return "Battery (simple)";
                case "V5 Battery Clip": return "Battery Clip";
                case "V5 Brain Mount Long": return "Brain Mount (long)";
                case "V5 Brain Mount Short": return "Brain Mount (short)";
                case "Bumper Switch - V1": return "Cortex Bumper Switch";
                case "Bumper Switch - V2": return "V5 Bumper Switch";
                case "Line Tracker - V1": return "Line Tracker";
                case "Line Tracker - V2": return "Line Tracker V2";
            }
            if ((m = Regex.Match(n, @"^V5 (Distance|GPS|Inertial|Optical|Rotation|Vision) Sensor$")).Success)
                return $"{m.Groups[1].Value} Sensor";

            // ---- nuts ("Keps Nut")
            if ((m = Regex.Match(n, @"^Nut (Keps|Hex|Nylock|Low Profile|Thin Nylock|M5 Thin Hex|Nylon)$")).Success)
                return $"{m.Groups[1].Value} Nut";
            switch (n)
            {
                case "Nut Alu": return "Aluminum Nut";
                case "Nut Nylon - Black": return "Black Nylon Nut";
                case "Nut Nylon Flange Thread-Forming - Grainger": return "Nylon Flange Thread-Forming Nut";
                case "Nut Nylon Thread-Forming - McMaster-Carr": return "Nylon Thread-Forming Nut";
            }

            // ---- bearings, collars, lock bars
            switch (n)
            {
                case "HS Pillow Bearing Block": return "HS Block Bearing";
                case "LS Pillow Bearing Block": return "Block Bearing";
                case "Bearing Flat Low Profile": return "Low Profile Bearing Flat";
                case "Lock Bar Metal": return "Metal Lock Bar";
                case "Lock Bar Plastic": return "Plastic Lock Bar";
                case "HS Shaft Collar": return "HS Clamping Shaft Collar";
                case "Low Profile HS Shaft Collar": return "Low Profile HS Clamping Shaft Collar";
                case "Shaft Collar Metal": return "Normal Clamping Shaft Collar";
                case "Shaft Collar Rubber": return "Rubber Shaft Collar";
                case "Drop off Cam": return "Drop Off Cam";
                case "Lead Screw Mounting Bracket": return "Lead Screw Bracket";
            }
            if ((m = Regex.Match(n, @"^Shaft Collar (Hex|Star) (Smooth|Threaded)$")).Success)
                return $"Shaft Collar ({m.Groups[1].Value.ToLowerInvariant()} screw{(m.Groups[2].Value == "Threaded" ? ", threads shown" : "")})";
            if ((m = Regex.Match(n, @"^Shaft Collar (Set Screw|No Screw)$")).Success)
                return $"Shaft Collar ({m.Groups[1].Value.ToLowerInvariant()})";
            if ((m = Regex.Match(n, @"^HS Lock Bar - (\d+) hole$")).Success)
                return $"HS Lock Bar ({m.Groups[1].Value} hole)";

            // ---- pneumatics ("25mm Piston (rod)", "V5 Pneumatic Air Reservoir")
            if ((m = Regex.Match(n, @"^(\d+mm) (?:Stroke Pneumatic Cylinder|Piston) (Body|Rod)$")).Success)
                return $"{m.Groups[1].Value} Piston ({m.Groups[2].Value.ToLowerInvariant()})";
            if ((m = Regex.Match(n, @"^(Single|Double) Acting Piston (Body|Rod)$")).Success)
                return $"Legacy {m.Groups[1].Value} Acting Piston ({m.Groups[2].Value.ToLowerInvariant()})";
            if (n == "Air Reservoir")
                return "V5 Pneumatic Air Reservoir";
            if (n == "Reservoir" && folder.Contains("Legacy"))
                return "Legacy Pneumatic Air Reservoir";

            // ---- hinges ("2-Hole Hinge (top)")
            if ((m = Regex.Match(n, @"^(Top|Bottom) (\d)$")).Success && folder.Contains("Hinges"))
                return $"{m.Groups[2].Value}-Hole Hinge ({m.Groups[1].Value.ToLowerInvariant()})";
            if ((m = Regex.Match(n, @"^Pin (\d)$")).Success && folder.Contains("Hinges"))
                return $"{m.Groups[1].Value}-Hole Hinge Pin";

            // ---- field parts known only by part number: say what they belong to
            if (Regex.IsMatch(n, @"^\d{3}-\d{4}-\d{3}"))
            {
                var group = folder.Substring(folder.LastIndexOf('/') + 1);
                var tier = Regex.Match(folder, @"Tier \d");
                return $"{(tier.Success ? folder.Split('/')[folder.Split('/').Length - 2] + " " + tier.Value : group)} {n.Replace('_', ' ')}";
            }
            return n;
        }

        static string Metal(string abbreviation) => abbreviation == "Alu" ? "Aluminum" : "Steel";

        /// <summary>
        /// The original's inch style: sixteenths below an inch as fractions (1/4"), whole and longer sizes with
        /// two decimals (2.50"); wheels and shafts drop the zeros (2.75", 4", 24").
        /// </summary>
        static string Inch(string number, bool wheel = false)
        {
            var value = float.Parse(number.StartsWith(".") ? "0" + number : number, CultureInfo.InvariantCulture);
            if (value < 1f && Units.Fraction(value) is { } fraction && fraction != "0")
                return fraction + "\"";
            return (wheel ? value.ToString("0.###", CultureInfo.InvariantCulture) : value.ToString("0.00", CultureInfo.InvariantCulture)) + "\"";
        }
    }
}
