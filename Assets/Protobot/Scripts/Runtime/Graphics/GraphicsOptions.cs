namespace Protobot.GraphicsQuality
{
    /// <summary>The quality presets shown in the settings menu, from weakest to strongest.</summary>
    public enum GraphicsPreset
    {
        VeryLow,
        Low,
        Medium,
        High,
        Ultra,
        Extreme,
        /// <summary>The player changed individual options so they no longer match a preset.</summary>
        Custom,
    }

    public enum ShadowQuality
    {
        Off,
        Low,
        Medium,
        High,
        Ultra,
    }

    public enum AntiAliasing
    {
        Off,
        FXAA,
        SMAA,
        MSAA2x,
        MSAA4x,
        MSAA8x,
        TAA,
    }

    /// <summary>
    /// Screen-space ambient occlusion (soft shading where surfaces meet).
    /// The value is also the index of the matching renderer in the URP asset's renderer list.
    /// </summary>
    public enum AmbientOcclusion
    {
        Off = 0,
        Medium = 1,
        High = 2,
    }

    /// <summary>The value is how many mipmap levels get skipped (0 = full resolution textures).</summary>
    public enum TextureQuality
    {
        Full = 0,
        Half = 1,
        Quarter = 2,
    }
}
