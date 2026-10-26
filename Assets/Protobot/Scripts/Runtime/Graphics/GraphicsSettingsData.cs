using System;
using UnityEngine;

namespace Protobot.GraphicsQuality
{
    /// <summary>
    /// Every graphics option the player can change. Plain data only: saving it and
    /// applying it to Unity are handled by <see cref="GraphicsSettingsManager"/>.
    /// </summary>
    [Serializable]
    public class GraphicsSettingsData
    {
        public const int CurrentVersion = 2;

        /// <summary>Bumped when the saved format changes, so old files can be upgraded.</summary>
        public int version = CurrentVersion;

        [Header("Quality (set by presets)")]
        public GraphicsPreset preset = GraphicsPreset.Medium;

        /// <summary>Resolution the 3D view renders at, relative to the window. Below 1 is upscaled with FSR, above 1 is supersampled.</summary>
        [Range(0.5f, 2f)] public float renderScale = 1f;
        public AntiAliasing antiAliasing = AntiAliasing.SMAA;
        public ShadowQuality shadows = ShadowQuality.Medium;
        public AmbientOcclusion ambientOcclusion = AmbientOcclusion.Off;
        public bool postProcessing = true;
        public TextureQuality textures = TextureQuality.Full;
        public ModelDetail modelDetail = ModelDetail.High;
        public bool anisotropicFiltering = true;
        public bool hdr = true;

        [Header("Display (never changed by presets)")]
        public FullScreenMode windowMode = FullScreenMode.FullScreenWindow;

        /// <summary>0 means use the monitor's native resolution.</summary>
        public int resolutionWidth;
        public int resolutionHeight;
        public bool vSync = true;

        /// <summary>0 means unlimited. Ignored while vSync is on.</summary>
        public int frameRateLimit;

        public GraphicsSettingsData Clone() => (GraphicsSettingsData)MemberwiseClone();

        /// <summary>Overwrites the quality options with the values of a preset. Display options are left alone.</summary>
        public void SetQualityFromPreset(GraphicsPreset newPreset)
        {
            preset = newPreset;

            switch (newPreset)
            {
                case GraphicsPreset.VeryLow:
                    renderScale = 0.7f;
                    antiAliasing = AntiAliasing.Off;
                    shadows = ShadowQuality.Off;
                    ambientOcclusion = AmbientOcclusion.Off;
                    postProcessing = false;
                    textures = TextureQuality.Half;
                    modelDetail = ModelDetail.Low;
                    anisotropicFiltering = false;
                    hdr = false;
                    break;

                case GraphicsPreset.Low:
                    renderScale = 0.85f;
                    antiAliasing = AntiAliasing.FXAA;
                    shadows = ShadowQuality.Low;
                    ambientOcclusion = AmbientOcclusion.Off;
                    postProcessing = true;
                    textures = TextureQuality.Full;
                    modelDetail = ModelDetail.Low;
                    anisotropicFiltering = false;
                    hdr = false;
                    break;

                case GraphicsPreset.Medium:
                    renderScale = 1f;
                    antiAliasing = AntiAliasing.SMAA;
                    shadows = ShadowQuality.Medium;
                    ambientOcclusion = AmbientOcclusion.Off;
                    postProcessing = true;
                    textures = TextureQuality.Full;
                    modelDetail = ModelDetail.Medium;
                    anisotropicFiltering = true;
                    hdr = true;
                    break;

                case GraphicsPreset.High:
                    renderScale = 1f;
                    antiAliasing = AntiAliasing.MSAA4x;
                    shadows = ShadowQuality.High;
                    ambientOcclusion = AmbientOcclusion.Medium;
                    postProcessing = true;
                    textures = TextureQuality.Full;
                    modelDetail = ModelDetail.High;
                    anisotropicFiltering = true;
                    hdr = true;
                    break;

                case GraphicsPreset.Ultra:
                    renderScale = 1f;
                    antiAliasing = AntiAliasing.MSAA4x;
                    shadows = ShadowQuality.Ultra;
                    ambientOcclusion = AmbientOcclusion.High;
                    postProcessing = true;
                    textures = TextureQuality.Full;
                    modelDetail = ModelDetail.Ultra;
                    anisotropicFiltering = true;
                    hdr = true;
                    break;

                case GraphicsPreset.Extreme:
                    // Renders at 1.5x the window resolution and scales down: very sharp, very expensive.
                    renderScale = 1.5f;
                    antiAliasing = AntiAliasing.MSAA4x;
                    shadows = ShadowQuality.Ultra;
                    ambientOcclusion = AmbientOcclusion.High;
                    postProcessing = true;
                    textures = TextureQuality.Full;
                    modelDetail = ModelDetail.Ultra;
                    anisotropicFiltering = true;
                    hdr = true;
                    break;
            }
        }

        /// <summary>Returns the preset whose quality options match these exactly, or Custom if none do.</summary>
        public GraphicsPreset DetectPreset()
        {
            var probe = Clone();

            for (var p = GraphicsPreset.VeryLow; p < GraphicsPreset.Custom; p++)
            {
                probe.SetQualityFromPreset(p);
                if (QualityEquals(probe))
                    return p;
            }

            return GraphicsPreset.Custom;
        }

        bool QualityEquals(GraphicsSettingsData other) =>
            Mathf.Approximately(renderScale, other.renderScale)
            && antiAliasing == other.antiAliasing
            && shadows == other.shadows
            && ambientOcclusion == other.ambientOcclusion
            && postProcessing == other.postProcessing
            && textures == other.textures
            && modelDetail == other.modelDetail
            && anisotropicFiltering == other.anisotropicFiltering
            && hdr == other.hdr;
    }
}
