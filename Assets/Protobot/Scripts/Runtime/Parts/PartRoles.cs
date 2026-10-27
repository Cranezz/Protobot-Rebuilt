namespace Protobot.Parts
{
    /// <summary>What a part is for when building: decides how its holes behave and how it snaps.</summary>
    public enum PartRole
    {
        Other,
        Structure,
        Screw,
        ShoulderScrew,
        Nut,
        Standoff,
        Spacer,       // spacers and washers
        Collar,
        Shaft,
        Gear,         // gears, sprockets, pulleys
        Wheel,
        Bearing,
        Motor,
        Electronics,
    }

    public static class PartRoles
    {
        /// <summary>The role of a part from its library path (e.g. "Structure/Hardware/Nut Keps").</summary>
        public static PartRole Of(string partId)
        {
            if (string.IsNullOrEmpty(partId))
                return PartRole.Other;
            var id = partId.ToLowerInvariant();
            var name = id.Substring(id.LastIndexOf('/') + 1);
            if (id.Contains("/hardware/"))
            {
                if (id.Contains("shoulder"))
                    return PartRole.ShoulderScrew;
                if (id.Contains("screw") && !id.Contains("thumbscrew"))
                    return PartRole.Screw;
                if (name.StartsWith("nut") || id.Contains("/nut"))
                    return PartRole.Nut;
            }
            if (id.Contains("standoff") && !id.Contains("retainer"))
                return PartRole.Standoff;
            if (id.Contains("collar"))
                return PartRole.Collar;
            if (id.Contains("spacer") || id.Contains("washer"))
                return PartRole.Spacer;
            if (id.Contains("bearing"))
                return PartRole.Bearing;
            if (id.Contains("motor"))
                return PartRole.Motor;
            if (id.StartsWith("motion/shaft"))
                return PartRole.Shaft;
            if (id.Contains("gear") || id.Contains("sprocket") || id.Contains("pulley") || id.Contains("pinion"))
                return PartRole.Gear;
            if (id.Contains("wheel"))
                return PartRole.Wheel;
            if (id.StartsWith("electronics"))
                return PartRole.Electronics;
            if (id.StartsWith("structure"))
                return PartRole.Structure;
            return PartRole.Other;
        }

        public static bool IsScrew(PartRole role) => role == PartRole.Screw || role == PartRole.ShoulderScrew;
    }
}
