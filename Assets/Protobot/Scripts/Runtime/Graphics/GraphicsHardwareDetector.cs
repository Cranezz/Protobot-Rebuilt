using UnityEngine;

namespace Protobot.GraphicsQuality
{
    /// <summary>
    /// Picks a sensible starting preset on first launch by looking at the graphics card.
    /// It's a rough guess on purpose: players can always change it afterwards.
    /// </summary>
    public static class GraphicsHardwareDetector
    {
        public static GraphicsPreset RecommendPreset()
        {
            var gpuName = SystemInfo.graphicsDeviceName.ToLowerInvariant();
            var vramMB = SystemInfo.graphicsMemorySize;
            var ramMB = SystemInfo.systemMemorySize;

            if (IsSoftwareRenderer(gpuName))
                return GraphicsPreset.VeryLow;

            // Integrated graphics share system RAM, so their reported VRAM isn't meaningful.
            if (IsIntegrated(gpuName))
                return ramMB < 8000 ? GraphicsPreset.VeryLow : GraphicsPreset.Low;

            if (vramMB < 2000) return GraphicsPreset.Low;
            if (vramMB < 4000) return GraphicsPreset.Medium;
            if (vramMB < 7000) return GraphicsPreset.High;

            // Extreme is never picked automatically; it's an opt-in for very strong PCs.
            return GraphicsPreset.Ultra;
        }

        static bool IsSoftwareRenderer(string gpuName) =>
            gpuName.Contains("microsoft basic render") || gpuName.Contains("llvmpipe") || gpuName.Contains("swiftshader");

        static bool IsIntegrated(string gpuName)
        {
            // Intel Arc cards are dedicated; every other Intel GPU is integrated.
            if (gpuName.Contains("intel"))
                return !gpuName.Contains("arc");

            // AMD APUs report as "Radeon(TM) Graphics", "Radeon Vega 8", "Radeon 780M" etc. Dedicated cards say "RX".
            if (gpuName.Contains("radeon"))
                return !gpuName.Contains("rx") && !gpuName.Contains("pro w");

            return false;
        }
    }
}
