using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Protobot.GraphicsQuality
{
    /// <summary>
    /// Loads, saves and applies the player's graphics settings.
    /// Starts by itself before the first scene loads, so no GameObject is needed.
    ///
    /// The URP asset in the project is never modified. At startup a runtime copy is made
    /// and all changes go to that copy, so playing in the editor doesn't dirty project files.
    /// </summary>
    public static class GraphicsSettingsManager
    {
        const string FolderName = "Settings";
        const string FileName = "graphics.json";

        static string FilePath => Path.Combine(Application.persistentDataPath, FolderName, FileName);

        /// <summary>The settings currently in effect. Treat as read-only; use <see cref="Apply"/> to change them.</summary>
        public static GraphicsSettingsData Current { get; private set; }

        /// <summary>Raised after new settings have been applied.</summary>
        public static event Action<GraphicsSettingsData> Changed;

        static RenderPipelineAsset originalQualityPipeline;
        static UniversalRenderPipelineAsset runtimePipeline;

        // Resets static state when entering Play mode with domain reload turned off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Current = null;
            Changed = null;
            originalQualityPipeline = null;
            runtimePipeline = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Application.quitting -= RestoreProjectPipeline;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            if (!CreateRuntimePipeline())
                return;

            SceneManager.sceneLoaded += OnSceneLoaded;
            Application.quitting += RestoreProjectPipeline;

            var settings = Load();
            var isFirstLaunch = settings == null;

            if (isFirstLaunch)
            {
                settings = new GraphicsSettingsData();
                settings.SetQualityFromPreset(GraphicsHardwareDetector.RecommendPreset());
                Debug.Log($"[Graphics] First launch on '{SystemInfo.graphicsDeviceName}', picked preset {settings.preset}.");
            }

            Apply(settings, save: isFirstLaunch);
        }

        /// <summary>Switches to a preset, keeping the current display options.</summary>
        public static void SetPreset(GraphicsPreset preset)
        {
            if (preset == GraphicsPreset.Custom || Current == null)
                return;

            var settings = Current.Clone();
            settings.SetQualityFromPreset(preset);
            Apply(settings);
        }

        /// <summary>Applies new settings to Unity and (by default) saves them to disk.</summary>
        public static void Apply(GraphicsSettingsData settings, bool save = true)
        {
            if (runtimePipeline == null)
                return;

            Current = settings.Clone();
            Current.preset = Current.DetectPreset();

            ApplyPipeline(Current);
            ApplyQualitySettings(Current);
            ApplyDisplay(Current);
            ApplyToSceneObjects();

            if (save)
                Save(Current);

            Changed?.Invoke(Current);
        }

        /// <summary>
        /// Applies camera-specific options. Called automatically for every camera when a scene loads;
        /// call it yourself for cameras created later at runtime.
        /// </summary>
        public static void ApplyToCamera(Camera camera)
        {
            if (Current == null || camera == null)
                return;

            var cameraData = camera.GetUniversalAdditionalCameraData();
            if (cameraData.renderType != CameraRenderType.Base)
                return;

            // The renderer list in the URP asset is ordered to match the AmbientOcclusion enum.
            cameraData.SetRenderer((int)Current.ambientOcclusion);
            cameraData.renderPostProcessing = Current.postProcessing;
            cameraData.renderShadows = Current.shadows != ShadowQuality.Off;
            cameraData.antialiasing = ToCameraAntiAliasing(Current.antiAliasing);
            cameraData.antialiasingQuality = AntialiasingQuality.High;
        }

        static bool CreateRuntimePipeline()
        {
            originalQualityPipeline = QualitySettings.renderPipeline;

            var activePipeline = originalQualityPipeline != null ? originalQualityPipeline : GraphicsSettings.defaultRenderPipeline;
            var projectPipeline = activePipeline as UniversalRenderPipelineAsset;
            if (projectPipeline == null)
            {
                Debug.LogError("[Graphics] No URP asset is assigned in Project Settings > Graphics/Quality. Graphics settings are disabled.");
                return false;
            }

            runtimePipeline = Object.Instantiate(projectPipeline);
            runtimePipeline.name = projectPipeline.name + " (Runtime)";
            QualitySettings.renderPipeline = runtimePipeline;
            return true;
        }

        // Puts the project's own URP asset back. Matters in the editor, where Play mode ends but the editor keeps running.
        static void RestoreProjectPipeline()
        {
            QualitySettings.renderPipeline = originalQualityPipeline;

            if (runtimePipeline != null)
                Object.Destroy(runtimePipeline);

            runtimePipeline = null;
        }

        static void ApplyPipeline(GraphicsSettingsData s)
        {
            var pipeline = runtimePipeline;

            pipeline.renderScale = s.renderScale;
            pipeline.upscalingFilter = s.renderScale < 1f ? UpscalingFilterSelection.FSR : UpscalingFilterSelection.Auto;
            pipeline.supportsHDR = s.hdr;

            // MSAA and TAA can't be combined, so MSAA is only on when it's the chosen mode.
            pipeline.msaaSampleCount = s.antiAliasing switch
            {
                AntiAliasing.MSAA2x => 2,
                AntiAliasing.MSAA4x => 4,
                AntiAliasing.MSAA8x => 8,
                _ => 1,
            };

            var shadows = ShadowSettings.For(s.shadows);
            pipeline.shadowDistance = shadows.distance;
            pipeline.shadowCascadeCount = shadows.cascades;
            pipeline.mainLightShadowmapResolution = shadows.resolution;
            pipeline.additionalLightsShadowmapResolution = shadows.resolution;
        }

        static void ApplyQualitySettings(GraphicsSettingsData s)
        {
            QualitySettings.globalTextureMipmapLimit = (int)s.textures;
            QualitySettings.lodBias = s.modelDetail switch
            {
                ModelDetail.Low => 0.5f,
                ModelDetail.Medium => 1f,
                ModelDetail.High => 1.75f,
                _ => 3f,
            };
            QualitySettings.anisotropicFiltering = s.anisotropicFiltering ? AnisotropicFiltering.ForceEnable : AnisotropicFiltering.Disable;
        }

        static void ApplyDisplay(GraphicsSettingsData s)
        {
            QualitySettings.vSyncCount = s.vSync ? 1 : 0;
            Application.targetFrameRate = s.vSync || s.frameRateLimit <= 0 ? -1 : s.frameRateLimit;

#if !UNITY_EDITOR
            // The editor's Game view controls its own size, so window/resolution only apply in builds.
            var width = s.resolutionWidth > 0 ? s.resolutionWidth : Display.main.systemWidth;
            var height = s.resolutionHeight > 0 ? s.resolutionHeight : Display.main.systemHeight;

            if (Screen.width != width || Screen.height != height || Screen.fullScreenMode != s.windowMode)
                Screen.SetResolution(width, height, s.windowMode);
#endif
        }

        static void ApplyToSceneObjects()
        {
            foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                ApplyToCamera(camera);

            var softShadowQuality = ShadowSettings.For(Current.shadows).softQuality;
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.shadows != LightShadows.None)
                    light.GetUniversalAdditionalLightData().softShadowQuality = softShadowQuality;
            }
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyToSceneObjects();

        static AntialiasingMode ToCameraAntiAliasing(AntiAliasing aa) => aa switch
        {
            AntiAliasing.FXAA => AntialiasingMode.FastApproximateAntialiasing,
            AntiAliasing.SMAA => AntialiasingMode.SubpixelMorphologicalAntiAliasing,
            AntiAliasing.TAA => AntialiasingMode.TemporalAntiAliasing,
            _ => AntialiasingMode.None,
        };

        static GraphicsSettingsData Load()
        {
            if (!File.Exists(FilePath))
                return null;

            try
            {
                var settings = JsonUtility.FromJson<GraphicsSettingsData>(File.ReadAllText(FilePath));
                if (settings == null || settings.version > GraphicsSettingsData.CurrentVersion)
                    return null;

                // Version 1 files predate Model Detail: fill it in from the saved preset.
                if (settings.version < 2 && settings.preset != GraphicsPreset.Custom)
                    settings.SetQualityFromPreset(settings.preset);

                settings.version = GraphicsSettingsData.CurrentVersion;
                return settings;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Graphics] Couldn't read {FilePath}, using defaults instead. {e.Message}");
                return null;
            }
        }

        static void Save(GraphicsSettingsData settings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonUtility.ToJson(settings, prettyPrint: true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Graphics] Couldn't save graphics settings to {FilePath}. {e.Message}");
            }
        }

        /// <summary>
        /// What each shadow quality level means. Distances are in scene units, which are inches in the Protobot
        /// scene (the original Protobot Rebuilt drew shadows out to 150); shorter packs more detail near the camera.
        /// </summary>
        readonly struct ShadowSettings
        {
            public readonly float distance;
            public readonly int cascades;
            public readonly int resolution;
            public readonly SoftShadowQuality softQuality;

            ShadowSettings(float distance, int cascades, int resolution, SoftShadowQuality softQuality)
            {
                this.distance = distance;
                this.cascades = cascades;
                this.resolution = resolution;
                this.softQuality = softQuality;
            }

            public static ShadowSettings For(ShadowQuality quality) => quality switch
            {
                ShadowQuality.Low => new ShadowSettings(40f, 1, 1024, SoftShadowQuality.Low),
                ShadowQuality.Medium => new ShadowSettings(75f, 2, 2048, SoftShadowQuality.Medium),
                ShadowQuality.High => new ShadowSettings(110f, 3, 2048, SoftShadowQuality.High),
                ShadowQuality.Ultra => new ShadowSettings(150f, 4, 4096, SoftShadowQuality.High),
                // Off: shadows are turned off on the camera; these values just keep the asset valid.
                _ => new ShadowSettings(40f, 1, 256, SoftShadowQuality.Low),
            };
        }
    }
}
