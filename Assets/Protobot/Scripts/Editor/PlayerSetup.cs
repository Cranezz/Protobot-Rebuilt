using Protobot.CameraControl;
using Protobot.Interaction;
using Protobot.InputHandling;
using Protobot.Menus;
using Protobot.Parts;
using Protobot.Player;
using Protobot.Tools;
using UnityEngine.Rendering.Universal;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.TextCore.LowLevel;

namespace Protobot.Editor
{
    /// <summary>
    /// Tools > Protobot > Setup Player: the first-person player (just inside the garage door) with the
    /// camera in its head, hands for clicking/grabbing, the HUD reticle, the part picker, the Esc menu,
    /// and the spawner that puts picked parts on the bench under the window. Safe to run again.
    /// </summary>
    public static class PlayerSetup
    {
        const string ActionsPath = "Assets/Protobot/Input/ProtobotControls.inputactions";
        const string ThemePath = "Assets/Protobot/UI/UiTheme.asset";
        const string FontFolder = "Assets/Protobot/Art/Fonts/";
        const string CatalogPath = "Assets/Protobot/Parts/PartCatalog.asset";
        const int IgnoreRaycastLayer = 2;
        static readonly Vector3 SpawnPosition = new(0f, 0f, -2.4f);

        // Where picked parts are dropped: the back bench, centred under the window (Unity space, metres).
        static readonly Vector3 DropPoint = new(0f, 1.0f, 2.86f);

        static readonly (CursorKind kind, string file, Vector2 hotspot)[] Cursors =
        {
            (CursorKind.Default, "cursor_default.png", new Vector2(3, 2)),
            (CursorKind.Point, "cursor_point.png", new Vector2(19, 3)),
            (CursorKind.Grab, "cursor_grab.png", new Vector2(24, 24)),
            (CursorKind.Grabbing, "cursor_grabbing.png", new Vector2(24, 26)),
        };

        [MenuItem("Tools/Protobot/Setup Player")]
        public static void Setup()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                Debug.LogError("[Player] No Main Camera in the scene.");
                return;
            }

            var theme = CreateTheme();

            // ---- body
            var player = GameObject.Find("Player") ?? new GameObject("Player");
            player.layer = IgnoreRaycastLayer;
            player.transform.SetPositionAndRotation(SpawnPosition, Quaternion.LookRotation(Vector3.forward));
            var body = GetOrAdd<CharacterController>(player);
            body.height = 1.62f;
            body.radius = 0.18f;
            body.center = new Vector3(0f, 0.81f, 0f);
            body.stepOffset = 0.2f;
            body.slopeLimit = 45f;
            body.skinWidth = 0.015f;
            body.minMoveDistance = 0f;

            var head = player.transform.Find("Head") ?? new GameObject("Head").transform;
            head.SetParent(player.transform, false);
            head.localPosition = new Vector3(0f, 1.5f, 0f);
            head.localRotation = Quaternion.Euler(10f, 0f, 0f);
            camera.transform.SetParent(head, false);
            camera.transform.localPosition = Vector3.zero;
            camera.transform.localRotation = Quaternion.identity;
            if (camera.TryGetComponent(out DevFlyCamera fly))
                Object.DestroyImmediate(fly);

            var motor = GetOrAdd<PlayerMotor>(player);
            SetFields(motor, ("head", head), ("standingHeight", 1.62f), ("crouchingHeight", 1.05f), ("eyeBelowTop", 0.12f));
            var input = GetOrAdd<DesktopPlayerInput>(player);
            SetFields(input, ("motor", motor), ("viewCamera", camera), ("actions", AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath)));

            // ---- UI
            var oldTooltip = GameObject.Find("HoverTooltip");
            if (oldTooltip != null)
                Object.DestroyImmediate(oldTooltip);
            var ui = GameObject.Find("UI") ?? new GameObject("UI");
            var hud = GetOrAdd<Hud>(Child(ui, "Hud"));
            SetFields(hud, ("theme", theme));

            var cursor = GetOrAdd<CursorController>(player);
            SetFields(cursor, ("viewCamera", camera), ("hud", hud), ("reach", 2.5f));
            SetCursorStyles(cursor);
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(player);  // the old PlayerHands

            // ---- spawning
            var spawned = GameObject.Find("SpawnedParts") ?? new GameObject("SpawnedParts");
            var spawnerGo = GameObject.Find("PartSpawner") ?? new GameObject("PartSpawner");
            var drop = spawnerGo.transform.Find("DropPoint") ?? new GameObject("DropPoint").transform;
            drop.SetParent(spawnerGo.transform, false);
            drop.SetPositionAndRotation(DropPoint, Quaternion.identity);
            var spawner = GetOrAdd<PartSpawner>(spawnerGo);
            SetFields(spawner, ("dropPoint", drop), ("spawnedParent", spawned.transform));

            var picker = GetOrAdd<PartPicker>(Child(ui, "PartPicker"));
            SetFields(picker, ("theme", theme), ("catalog", AssetDatabase.LoadAssetAtPath<PartCatalog>(CatalogPath)), ("spawner", spawner), ("hud", hud));
            var pause = GetOrAdd<PauseMenu>(Child(ui, "PauseMenu"));
            SetFields(pause, ("theme", theme));

            SetupTools(player, camera, head, cursor, hud, ui, theme);
            SetupScrapBins();
            SetupOutline();

            var events = GameObject.Find("EventSystem") ?? new GameObject("EventSystem");
            GetOrAdd<EventSystem>(events);
            if (events.TryGetComponent(out StandaloneInputModule legacy))
                Object.DestroyImmediate(legacy);
            GetOrAdd<InputSystemUIInputModule>(events);

            EditorSceneManager.MarkSceneDirty(player.scene);
            Debug.Log("[Player] Set up: mouse look, WASD, Shift sprint, C crouch, Space jump, hold left click to grab, Esc menu.");
        }

        // ---------------------------------------------------------------- outline

        const string RendererFolder = "Assets/Protobot/Settings/Rendering/";

        /// <summary>Adds the hover outline (Protobot.Rendering.OutlineFeature) to every URP renderer, once.</summary>
        static void SetupOutline()
        {
            var mask = Shader.Find("Hidden/Protobot/OutlineMask");
            var composite = Shader.Find("Hidden/Protobot/Outline");
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { RendererFolder }))
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                var feature = data.rendererFeatures.Find(f => f is Protobot.Rendering.OutlineFeature) as Protobot.Rendering.OutlineFeature;
                if (feature == null)
                {
                    // The same steps as the renderer's "Add Renderer Feature" button: a sub-asset, listed with its file id.
                    feature = ScriptableObject.CreateInstance<Protobot.Rendering.OutlineFeature>();
                    feature.name = "Outline";
                    AssetDatabase.AddObjectToAsset(feature, data);
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
                    var so = new SerializedObject(data);
                    var features = so.FindProperty("m_RendererFeatures");
                    var map = so.FindProperty("m_RendererFeatureMap");
                    features.arraySize++;
                    features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
                    map.arraySize++;
                    map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                feature.SetShaders(mask, composite);
                EditorUtility.SetDirty(feature);
                EditorUtility.SetDirty(data);
            }
            AssetDatabase.SaveAssets();
        }

        // ---------------------------------------------------------------- scrap bins

        const string ScrapBinModel = "Assets/Protobot/Art/Environment/Models/ScrapBin.fbx";

        // The big scrap bin on the floor just left of where picked parts land, its label facing the room.
        static readonly Vector3 ScrapBinPosition = new(-1.45f, 0f, 2.15f);

        // The workshop's small decorative can (front left, part of Workshop.fbx) works as a bin too.
        static readonly Vector3 DecorCanPosition = new(-2.92f, 0f, -1.5f);

        static void SetupScrapBins()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ScrapBinModel);
            if (model == null)
            {
                Debug.LogWarning("[Player] ScrapBin.fbx missing (run Tools/Blender/scrap_bin.py in Blender).");
                return;
            }
            var bin = GameObject.Find("ScrapBin");
            if (bin == null)
            {
                bin = (GameObject)PrefabUtility.InstantiatePrefab(model);
                bin.name = "ScrapBin";
            }
            // Keep the FBX root's own rotation (Blender's Z-up to Unity's Y-up), or the bin lies on its side.
            bin.transform.SetPositionAndRotation(ScrapBinPosition, Quaternion.Euler(0f, 180f, 0f) * model.transform.localRotation);
            AddBinTrigger(bin.transform, "Trash Trigger", new Vector3(0f, 0.42f, 0f), 0.24f, 0.62f);

            var decor = GameObject.Find("DecorCanTrigger") ?? new GameObject("DecorCanTrigger");
            decor.transform.SetPositionAndRotation(DecorCanPosition, Quaternion.identity);
            AddBinTrigger(decor.transform, "Trash Trigger", new Vector3(0f, 0.5f, 0f), 0.17f, 0.45f);
        }

        static void AddBinTrigger(Transform parent, string name, Vector3 centre, float radius, float height)
        {
            var trigger = parent.Find(name)?.gameObject ?? new GameObject(name);
            trigger.transform.SetParent(parent, false);
            trigger.transform.localPosition = Vector3.zero;
            trigger.transform.rotation = Quaternion.identity; // upright even under a rotated FBX root
            var capsule = GetOrAdd<CapsuleCollider>(trigger);
            capsule.isTrigger = true;
            capsule.center = centre;
            capsule.radius = radius;
            capsule.height = Mathf.Max(height, radius * 2f);
            GetOrAdd<ScrapBin>(trigger);
        }

        // ---------------------------------------------------------------- tools

        const string ToolPrefabFolder = "Assets/Protobot/Prefabs/Tools/";
        const int ViewModelLayer = 9;

        // How each tool sits in the hand, relative to the view-model anchor (lower right of the view).
        // Shown smaller than life: they're drawn by their own camera, so this only changes how much
        // screen they take up.
        static readonly (string model, float scale, Vector3 position, Vector3 rotation)[] ViewModels =
        {
            ("HackSaw", 0.5f, new Vector3(0.1f, 0.11f, 0f), new Vector3(-6f, -32f, -14f)),
            ("SprayGun", 0.62f, new Vector3(0.08f, 0.12f, 0f), new Vector3(-4f, -28f, 4f)),
            ("Drill", 0.62f, new Vector3(0.08f, 0.17f, 0f), new Vector3(-4f, -28f, 4f)),
        };

        static void SetupTools(GameObject player, Camera camera, Transform head, CursorController cursor, Hud hud, GameObject ui, UiTheme theme)
        {
            SetLayerName(ViewModelLayer, "ViewModel");
            IncludeRuntimeShaders();

            // Hand-held tools render in their own overlay camera so they never poke into benches or walls.
            camera.cullingMask &= ~(1 << ViewModelLayer);
            var vmCameraGo = camera.transform.Find("ViewModelCamera")?.gameObject ?? new GameObject("ViewModelCamera");
            vmCameraGo.transform.SetParent(camera.transform, false);
            var vmCamera = GetOrAdd<Camera>(vmCameraGo);
            vmCamera.cullingMask = 1 << ViewModelLayer;
            vmCamera.fieldOfView = 55f;
            vmCamera.nearClipPlane = 0.01f;
            vmCamera.farClipPlane = 3f;
            var vmData = vmCamera.GetUniversalAdditionalCameraData();
            vmData.renderType = CameraRenderType.Overlay;
            vmData.renderShadows = false;
            var mainData = camera.GetUniversalAdditionalCameraData();
            if (!mainData.cameraStack.Contains(vmCamera))
                mainData.cameraStack.Add(vmCamera);
            var anchor = camera.transform.Find("ViewModelAnchor") ?? new GameObject("ViewModelAnchor").transform;
            anchor.SetParent(camera.transform, false);
            anchor.localPosition = new Vector3(0.17f, -0.16f, 0.34f);
            anchor.localRotation = Quaternion.identity;
            anchor.gameObject.layer = ViewModelLayer;

            var prefabs = new GameObject[ViewModels.Length];
            System.IO.Directory.CreateDirectory(ToolPrefabFolder);
            for (var i = 0; i < ViewModels.Length; i++)
            {
                var (model, scale, position, rotation) = ViewModels[i];
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentImport.ToolModelRoot + model + ".fbx");
                if (asset == null)
                    continue;
                var root = new GameObject("ViewModel_" + model);
                root.transform.localScale = Vector3.one * scale;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.transform);
                // The models point along +Z with +Y up (Blender's +Y forward); keep the FBX's own axis
                // rotation underneath the pose.
                instance.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(rotation) * asset.transform.localRotation);
                foreach (var r in root.GetComponentsInChildren<Renderer>())
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                prefabs[i] = PrefabUtility.SaveAsPrefabAsset(root, ToolPrefabFolder + root.name + ".prefab");
                Object.DestroyImmediate(root);
            }

            // Menus the tools open.
            var sawMenu = GetOrAdd<SawMenu>(Child(ui, "SawMenu"));
            SetFields(sawMenu, ("theme", theme));
            var colorMenu = GetOrAdd<ColorPickerMenu>(Child(ui, "ColorPicker"));
            SetFields(colorMenu, ("theme", theme));
            var bitMenu = GetOrAdd<DrillBitMenu>(Child(ui, "DrillBitMenu"));
            SetFields(bitMenu, ("theme", theme));

            var toolsRoot = Child(player, "Tools");
            var crosshair = Sprite("ui_crosshair");
            var hands = GetOrAdd<HandsTool>(Child(toolsRoot, "Hands"));
            SetFields(hands, ("displayName", "Hands"), ("icon", AssetDatabase.LoadAssetAtPath<Sprite>(UiTextureImport.CursorFolder + "cursor_grab.png")), ("head", head));
            var saw = GetOrAdd<SawTool>(Child(toolsRoot, "HackSaw"));
            SetFields(saw, ("displayName", "Hack Saw"), ("icon", Sprite("tool_saw")), ("viewModelPrefab", prefabs[0]), ("menu", sawMenu), ("crosshair", crosshair));
            var paint = GetOrAdd<PaintTool>(Child(toolsRoot, "SprayPaint"));
            SetFields(paint, ("displayName", "Spray Paint"), ("icon", Sprite("tool_paint")), ("viewModelPrefab", prefabs[1]), ("picker", colorMenu), ("crosshair", crosshair));
            var drill = GetOrAdd<DrillTool>(Child(toolsRoot, "Drill"));
            SetFields(drill, ("displayName", "Drill"), ("icon", Sprite("tool_drill")), ("viewModelPrefab", prefabs[2]), ("bitMenu", bitMenu), ("crosshair", crosshair));

            var belt = GetOrAdd<ToolBelt>(player);
            SetFields(belt, ("cursor", cursor), ("hud", hud), ("viewCamera", camera), ("viewModelAnchor", anchor));
            var so = new SerializedObject(belt);
            var list = so.FindProperty("tools");
            PlayerTool[] tools = { hands, saw, paint, drill };
            list.arraySize = tools.Length;
            for (var i = 0; i < tools.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = tools[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            var toolHud = GetOrAdd<ToolHud>(Child(ui, "ToolHud"));
            SetFields(toolHud, ("theme", theme), ("belt", belt), ("handsIcon", AssetDatabase.LoadAssetAtPath<Sprite>(UiTextureImport.CursorFolder + "cursor_grab.png")));
        }

        // The tools make their preview materials at runtime with Shader.Find; a build strips any shader that
        // no material uses, so these are listed under Project Settings > Graphics > Always Included Shaders.
        static readonly string[] RuntimeShaders =
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Unlit",
            "Universal Render Pipeline/Particles/Unlit",
        };

        static void IncludeRuntimeShaders()
        {
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var list = settings.FindProperty("m_AlwaysIncludedShaders");
            foreach (var name in RuntimeShaders)
            {
                var shader = Shader.Find(name);
                if (shader == null)
                    continue;
                var present = false;
                for (var i = 0; i < list.arraySize && !present; i++)
                    present = list.GetArrayElementAtIndex(i).objectReferenceValue == shader;
                if (present)
                    continue;
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            }
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetLayerName(int layer, string name)
        {
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            tags.FindProperty("layers").GetArrayElementAtIndex(layer).stringValue = name;
            tags.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Creates (once) the UI theme asset with the Barlow Condensed fonts and generated sprites.</summary>
        public static UiTheme CreateTheme()
        {
            var theme = AssetDatabase.LoadAssetAtPath<UiTheme>(ThemePath);
            if (theme == null)
            {
                System.IO.Directory.CreateDirectory("Assets/Protobot/UI");
                theme = ScriptableObject.CreateInstance<UiTheme>();
                AssetDatabase.CreateAsset(theme, ThemePath);
            }
            theme.font = FontAsset("BarlowCondensed-Medium");
            theme.boldFont = FontAsset("BarlowCondensed-Bold");
            theme.rounded = Sprite("ui_rounded");
            theme.roundedOutline = Sprite("ui_rounded_outline");
            theme.hazardStripes = Sprite("ui_hazard");
            theme.shadow = Sprite("ui_shadow");
            theme.reticleDot = Sprite("ui_dot");
            EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();
            return theme;
        }

        static TMP_FontAsset FontAsset(string name)
        {
            var path = FontFolder + name + " SDF.asset";
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (asset != null)
                return asset;

            var font = AssetDatabase.LoadAssetAtPath<Font>(FontFolder + name + ".ttf");
            asset = TMP_FontAsset.CreateFontAsset(font, 72, 8, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            asset.name = name + " SDF";
            AssetDatabase.CreateAsset(asset, path);
            asset.atlasTextures[0].name = name + " Atlas";
            AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
            asset.material.name = name + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            // Pre-fill the characters the UI uses so the first frame doesn't rebuild the atlas.
            asset.TryAddCharacters(" !\"#%&'()*+,-./0123456789:;<=>?ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz×°·…", out _);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        static Sprite Sprite(string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>(UiTextureImport.SpriteFolder + name + ".png");

        static void SetCursorStyles(CursorController cursor)
        {
            var so = new SerializedObject(cursor);
            var styles = so.FindProperty("styles");
            styles.arraySize = Cursors.Length;
            for (var i = 0; i < Cursors.Length; i++)
            {
                var element = styles.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("kind").enumValueIndex = (int)Cursors[i].kind;
                element.FindPropertyRelative("sprite").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<Sprite>(UiTextureImport.CursorFolder + Cursors[i].file);
                element.FindPropertyRelative("hotspot").vector2Value = Cursors[i].hotspot;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject Child(GameObject parent, string name)
        {
            var t = parent.transform.Find(name);
            if (t != null)
                return t.gameObject;
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        static T GetOrAdd<T>(GameObject go) where T : Component =>
            go.TryGetComponent(out T existing) ? existing : go.AddComponent<T>();

        static void SetFields(Object target, params (string name, object value)[] fields)
        {
            var so = new SerializedObject(target);
            foreach (var (name, value) in fields)
            {
                var p = so.FindProperty(name);
                switch (value)
                {
                    case Object o: p.objectReferenceValue = o; break;
                    case float f: p.floatValue = f; break;
                    case int i: p.intValue = i; break;
                    case string str: p.stringValue = str; break;
                    case bool b: p.boolValue = b; break;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
