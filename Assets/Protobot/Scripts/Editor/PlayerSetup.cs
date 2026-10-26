using Protobot.CameraControl;
using Protobot.Interaction;
using Protobot.InputHandling;
using Protobot.Menus;
using Protobot.Parts;
using Protobot.Player;
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
            var hands = GetOrAdd<PlayerHands>(player);
            SetFields(hands, ("cursor", cursor), ("head", head));

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

            var events = GameObject.Find("EventSystem") ?? new GameObject("EventSystem");
            GetOrAdd<EventSystem>(events);
            if (events.TryGetComponent(out StandaloneInputModule legacy))
                Object.DestroyImmediate(legacy);
            GetOrAdd<InputSystemUIInputModule>(events);

            EditorSceneManager.MarkSceneDirty(player.scene);
            Debug.Log("[Player] Set up: mouse look, WASD, Shift sprint, C crouch, Space jump, hold left click to grab, Esc menu.");
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
                    case bool b: p.boolValue = b; break;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
