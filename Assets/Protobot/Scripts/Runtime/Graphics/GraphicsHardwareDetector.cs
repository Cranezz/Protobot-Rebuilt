using System.Text.RegularExpressions;
using UnityEngine;

namespace Protobot.GraphicsQuality
{
    /// <summary>
    /// Picks a sensible starting preset on first launch by looking at the graphics card.
    /// It's a rough guess on purpose: players can always change it afterwards, or run the benchmark
    /// (Graphics > Benchmark), which measures instead of guessing.
    ///
    /// Builds are millions of triangles (every part is the full VEX model), so what limits a weak graphics card is
    /// geometry, which old workstation cards (Quadro K/M/P, FirePro) and integrated graphics have little of,
    /// whatever their memory says. Known families are matched by name first; memory size is the fallback.
    /// </summary>
    public static class GraphicsHardwareDetector
    {
        public static GraphicsPreset RecommendPreset() =>
            Recommend(SystemInfo.graphicsDeviceName, SystemInfo.graphicsMemorySize, SystemInfo.systemMemorySize);

        public static GraphicsPreset Recommend(string deviceName, int vramMB, int ramMB)
        {
            var name = deviceName.ToLowerInvariant();
            var preset = ByName(name) ?? ByMemory(vramMB);

            // Little system memory: the build, the parts and the graphics driver all share it.
            if (ramMB > 0 && ramMB < 6000 && preset > GraphicsPreset.VeryLow)
                preset = GraphicsPreset.VeryLow;
            else if (ramMB > 0 && ramMB < 12000 && preset > GraphicsPreset.Medium)
                preset = GraphicsPreset.Medium;
            return preset;
        }

        /// <summary>A short description of the kind of graphics card, for the settings window.</summary>
        public static string Describe(string deviceName)
        {
            var name = deviceName.ToLowerInvariant();
            if (IsSoftwareRenderer(name)) return "Software rendering (no graphics driver)";
            if (IsIntegrated(name)) return "Integrated graphics";
            if (Regex.IsMatch(name, @"quadro|firepro|radeon pro|nvidia [atp]\d{3,4}\b|rtx a\d")) return "Workstation graphics card";
            return "Graphics card";
        }

        static GraphicsPreset? ByName(string name)
        {
            if (IsSoftwareRenderer(name))
                return GraphicsPreset.VeryLow;

            // ---- Intel
            if (name.Contains("intel"))
            {
                if (name.Contains("arc"))
                    return Regex.IsMatch(name, @"\b[ab][57]\d0") ? GraphicsPreset.High : GraphicsPreset.Medium;  // A770/B580; A380, Arc iGPUs
                if (name.Contains("xe") || Regex.IsMatch(name, @"iris.*(plus|pro)?.*6\d\d"))
                    return GraphicsPreset.Low;         // Iris Xe (2020+ laptops)
                return GraphicsPreset.VeryLow;         // HD / UHD Graphics (old workstations, cheap laptops)
            }

            // ---- AMD integrated (APUs) and old/workstation cards
            if (name.Contains("radeon") || name.Contains("amd") || name.Contains("firepro"))
            {
                if (Regex.IsMatch(name, @"\b(7[4-9]0m|8[4-9]0m|6[6-8]0m)\b")) return GraphicsPreset.Medium;  // 680M/780M/890M
                if (name.Contains("firepro") || Regex.IsMatch(name, @"\bhd \d{4}|\br[579] \d{3}\b")) return GraphicsPreset.Low;
                if (Regex.IsMatch(name, @"rx 5[56]0\b|rx 64\d0|rx 65\d0")) return GraphicsPreset.Medium;
                if (Regex.IsMatch(name, @"rx 5[789]0\b|rx vega|rx 5[5-7]00|rx 66\d0|rx 76\d0")) return GraphicsPreset.High;
                if (Regex.IsMatch(name, @"rx [5-9]\d{3}")) return GraphicsPreset.Ultra;
                if (IsIntegrated(name)) return GraphicsPreset.Low;  // Vega 3-11, "Radeon Graphics"
                return null;
            }

            // ---- NVIDIA
            if (name.Contains("nvidia") || name.Contains("geforce") || name.Contains("quadro"))
            {
                if (Regex.IsMatch(name, @"\bgt \d{3,4}|\bmx\s?\d{3}|nvs ")) return GraphicsPreset.VeryLow;        // GT 710/1030, MX laptops
                if (Regex.IsMatch(name, @"quadro (k([1-4]\d{3}|\d{3})\b|[mp][45]\d{2}|m[12]\d{3}|fx|nvs)")) return GraphicsPreset.Low;  // K620/K2200, P400-P620
                if (Regex.IsMatch(name, @"quadro (p1000|p2000|p2200|m[45]000|k[56]\d{3})|t[46]00|t1000|t1200|a[245]00\b")) return GraphicsPreset.Medium;
                if (Regex.IsMatch(name, @"gtx (6|7)\d{2}\b")) return GraphicsPreset.Low;
                if (Regex.IsMatch(name, @"gtx (9[56]0|10[45]0)|gtx 16[56]0|rtx 3050 laptop|rtx 2050")) return GraphicsPreset.Medium;
                if (Regex.IsMatch(name, @"gtx (9[78]0|10[678]0)|gtx 16[6-9]0|rtx (20[67]0|3050|3060 laptop|4050)|quadro (p4000|p5000|rtx [45]000)|rtx a[234]000")) return GraphicsPreset.High;
                if (Regex.IsMatch(name, @"rtx [2-9]\d{3}|rtx a[4-6]000|titan")) return GraphicsPreset.Ultra;
                return null;
            }

            return null;
        }

        static GraphicsPreset ByMemory(int vramMB)
        {
            if (vramMB <= 0 || vramMB < 1500) return GraphicsPreset.VeryLow;
            if (vramMB < 3000) return GraphicsPreset.Low;
            if (vramMB < 5000) return GraphicsPreset.Medium;
            if (vramMB < 7000) return GraphicsPreset.High;
            // Extreme is never picked automatically; it's an opt-in for very strong PCs.
            return GraphicsPreset.Ultra;
        }

        static bool IsSoftwareRenderer(string gpuName) =>
            gpuName.Contains("microsoft basic render") || gpuName.Contains("llvmpipe") || gpuName.Contains("swiftshader") || gpuName.Contains("warp");

        static bool IsIntegrated(string gpuName)
        {
            // Intel Arc cards are dedicated; every other Intel GPU is integrated.
            if (gpuName.Contains("intel"))
                return !gpuName.Contains("arc") || gpuName.Contains("arc(tm) graphics") || gpuName.Contains("arc graphics");

            // AMD APUs report as "Radeon(TM) Graphics", "Radeon Vega 8", "Radeon 780M" etc. Dedicated cards say "RX".
            if (gpuName.Contains("radeon"))
                return !gpuName.Contains("rx") && !gpuName.Contains("pro w") && !gpuName.Contains("firepro") && !Regex.IsMatch(gpuName, @"\bhd \d{4}|\br[579] \d{3}");

            return false;
        }
    }
}
