using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Protobot.Editor
{
    /// <summary>
    /// Sets up the Main scene around the workshop model: static flags, lights, sky, probes,
    /// post-processing and lightmap settings. Safe to run again; it replaces what it made before.
    ///
    /// Lighting plan (realistic but cheap at runtime):
    ///  - Ceiling fluorescents are baked area lights (soft light and shadows, free at runtime).
    ///  - The sun through the window and a lamp over the build table are Mixed lights:
    ///    realtime direct light + shadows for the robot parts, baked bounce light.
    ///  - Light probes carry the baked bounce light onto moving parts; a box-projected
    ///    reflection probe gives the metal parts believable reflections.
    /// </summary>
    public static class WorkshopSceneSetup
    {
        const string ModelPath = "Assets/Protobot/Art/Environment/Models/Workshop.fbx";
        const string SkyTexturePath = "Assets/Protobot/Art/Environment/Textures/Sky/kloofendal_48d_partly_cloudy_2k.hdr";
        const string SkyMaterialPath = "Assets/Protobot/Art/Environment/Materials/Sky_Kloofendal.mat";
        const string LightingSettingsPath = "Assets/Protobot/Settings/Rendering/WorkshopLighting.lighting";
        const string VolumeProfilePath = "Assets/Protobot/Settings/Rendering/MainSceneProfile.asset";

        // Room interior bounds in Unity space (metres).
        static readonly Bounds Room = new Bounds(new Vector3(0f, 1.45f, 0f), new Vector3(6.4f, 2.9f, 6.4f));

        // Ceiling fixture centres (Unity space): Blender (x, y) -> Unity (x, z).
        static readonly Vector2[] Fixtures = { new(-1.4f, -1.0f), new(1.4f, -1.0f), new(-1.4f, 1.4f), new(1.4f, 1.4f) };

        [MenuItem("Tools/Protobot/Setup Workshop Scene")]
        public static void Setup()
        {
            var environment = FindOrCreate("Environment");
            var workshop = PlaceWorkshop(environment.transform);
            CreateLights(FindOrCreate("Lighting").transform);
            CreateProbes(FindOrCreate("Probes").transform);
            SetupSky();
            SetupPostProcessing();
            SetupLightmapping();
            SetupCamera();
            EditorSceneManager.MarkSceneDirty(workshop.scene);
            Debug.Log("[Workshop] Scene set up. Run Tools > Protobot > Bake Workshop Lighting to bake.");
        }

        [MenuItem("Tools/Protobot/Bake Workshop Lighting")]
        public static void Bake() => Lightmapping.BakeAsync();

        static GameObject FindOrCreate(string name) => GameObject.Find(name) ?? new GameObject(name);

        static void ClearChildren(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(parent.GetChild(i).gameObject);
        }

        static GameObject PlaceWorkshop(Transform parent)
        {
            var old = parent.Find("Workshop");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var workshop = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            workshop.name = "Workshop";

            const StaticEditorFlags staticFlags = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic |
                                                  StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic |
                                                  StaticEditorFlags.ReflectionProbeStatic;
            foreach (var t in workshop.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, staticFlags);

            // Glass lets the sun through: no shadows, and it shouldn't block baked light.
            foreach (var r in workshop.GetComponentsInChildren<MeshRenderer>(true))
            {
                var isGlass = r.sharedMaterial != null && r.sharedMaterial.name.StartsWith("Glass_");
                if (isGlass)
                {
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, staticFlags & ~StaticEditorFlags.ContributeGI);
                }
            }

            return workshop;
        }

        static void CreateLights(Transform parent)
        {
            ClearChildren(parent);
            var oldSun = GameObject.Find("Directional Light");
            if (oldSun != null)
                Object.DestroyImmediate(oldSun);

            // Late-morning sun coming through the back window, falling onto the floor and table.
            var sun = NewLight("Sun", LightType.Directional, parent);
            sun.lightmapBakeType = LightmapBakeType.Mixed;
            sun.color = new Color(1f, 0.94f, 0.85f);
            sun.intensity = 2.4f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(0.25f, -0.55f, -0.8f));

            // Fluorescent fixtures: baked rectangle area lights just under each tube.
            for (var i = 0; i < Fixtures.Length; i++)
            {
                var area = NewLight($"CeilingLight_{i}", LightType.Rectangle, parent);
                area.lightmapBakeType = LightmapBakeType.Baked;
                area.areaSize = new Vector2(1.2f, 0.12f);
                area.color = new Color(1f, 0.97f, 0.9f);
                area.intensity = 60f;
                area.range = 8f;
                area.transform.SetPositionAndRotation(new Vector3(Fixtures[i].x, 2.43f, Fixtures[i].y), Quaternion.Euler(90f, 0f, 0f));
            }

            // Work light over the build table: realtime shadows and highlights for parts being built.
            var lamp = NewLight("BuildTableLight", LightType.Spot, parent);
            lamp.lightmapBakeType = LightmapBakeType.Mixed;
            lamp.color = new Color(1f, 0.97f, 0.92f);
            lamp.intensity = 4f;
            lamp.range = 5f;
            lamp.spotAngle = 110f;
            lamp.innerSpotAngle = 70f;
            lamp.shadows = LightShadows.Soft;
            lamp.transform.SetPositionAndRotation(new Vector3(0f, 2.45f, 0.25f), Quaternion.Euler(90f, 0f, 0f));
        }

        static Light NewLight(string name, LightType type, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var light = go.AddComponent<Light>();
            light.type = type;
            go.AddComponent<UniversalAdditionalLightData>();
            return light;
        }

        static void CreateProbes(Transform parent)
        {
            ClearChildren(parent);

            var probesGo = new GameObject("LightProbes");
            probesGo.transform.SetParent(parent, false);
            var group = probesGo.AddComponent<LightProbeGroup>();
            var positions = new List<Vector3>();
            float[] heights = { 0.25f, 0.95f, 1.7f, 2.5f };
            for (var x = 0; x < 6; x++)
                for (var z = 0; z < 6; z++)
                    foreach (var y in heights)
                        positions.Add(new Vector3(Mathf.Lerp(-2.9f, 2.9f, x / 5f), y, Mathf.Lerp(-2.9f, 2.9f, z / 5f)));
            group.probePositions = positions.ToArray();

            var reflectionGo = new GameObject("RoomReflections");
            reflectionGo.transform.SetParent(parent, false);
            reflectionGo.transform.position = new Vector3(0f, 1.6f, 0f);
            var probe = reflectionGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.size = Room.size;
            probe.center = Room.center - reflectionGo.transform.position;
            probe.boxProjection = true;
            probe.resolution = 256;
            probe.hdr = true;
        }

        static void SetupSky()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Skybox/Panoramic"));
                AssetDatabase.CreateAsset(material, SkyMaterialPath);
            }
            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture>(SkyTexturePath));
            material.SetFloat("_Exposure", 1f);
            material.SetFloat("_Rotation", 200f);
            EditorUtility.SetDirty(material);

            RenderSettings.skybox = material;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.sun = GameObject.Find("Sun")?.GetComponent<Light>();
        }

        static void SetupPostProcessing()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
                return;

            if (profile.TryGet(out Tonemapping tonemapping))
            {
                tonemapping.mode.overrideState = true;
                tonemapping.mode.value = TonemappingMode.Neutral;
            }
            if (profile.TryGet(out Bloom bloom))
            {
                bloom.threshold.overrideState = true;
                bloom.threshold.value = 1.1f;
                bloom.intensity.overrideState = true;
                bloom.intensity.value = 0.25f;
            }
            if (profile.TryGet(out Vignette vignette))
            {
                vignette.intensity.overrideState = true;
                vignette.intensity.value = 0.15f;
            }
            if (!profile.TryGet(out ColorAdjustments color))
                color = profile.Add<ColorAdjustments>(true);
            color.postExposure.overrideState = true;
            color.postExposure.value = 0.35f;
            color.contrast.overrideState = true;
            color.contrast.value = 8f;
            color.saturation.overrideState = true;
            color.saturation.value = 5f;
            EditorUtility.SetDirty(profile);
        }

        static void SetupLightmapping()
        {
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
            if (settings == null)
            {
                settings = new LightingSettings { name = "WorkshopLighting" };
                AssetDatabase.CreateAsset(settings, LightingSettingsPath);
            }

            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
            settings.directSampleCount = 64;
            settings.indirectSampleCount = 512;
            settings.environmentSampleCount = 256;
            settings.maxBounces = 3;
            settings.indirectScale = 1.3f;
            settings.lightmapResolution = 20f;
            settings.lightmapPadding = 4;
            settings.lightmapMaxSize = 2048;
            settings.directionalityMode = LightmapsMode.CombinedDirectional;
            settings.ao = true;
            settings.aoMaxDistance = 0.6f;
            settings.lightmapCompression = LightmapCompression.HighQuality;
            settings.filteringMode = LightingSettings.FilterMode.Auto;
            Lightmapping.lightingSettings = settings;
            EditorUtility.SetDirty(settings);
        }

        static void SetupCamera()
        {
            var camera = Camera.main;
            if (camera == null)
                return;

            camera.fieldOfView = 65f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 200f;
            camera.clearFlags = CameraClearFlags.Skybox;

            // Once the player exists the camera lives in its head (Tools > Protobot > Setup Player).
            if (camera.transform.parent != null)
                return;

            // Standing just inside the garage door, looking at the back bench.
            camera.transform.SetPositionAndRotation(new Vector3(0f, 1.65f, -2.6f), Quaternion.LookRotation(new Vector3(0f, 1.05f, 2.5f) - new Vector3(0f, 1.65f, -2.6f)));
        }
    }
}
