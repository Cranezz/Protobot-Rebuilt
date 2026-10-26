using Protobot.CameraControl;
using Protobot.InputHandling;
using Protobot.Interaction;
using Protobot.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.Editor
{
    /// <summary>
    /// Tools > Protobot > Setup Player: puts a first-person player in the workshop (just inside the
    /// garage door), moves the main camera into its head, and adds the dynamic cursor + tooltip.
    /// Safe to run again.
    /// </summary>
    public static class PlayerSetup
    {
        const string ActionsPath = "Assets/Protobot/Input/ProtobotControls.inputactions";
        const int IgnoreRaycastLayer = 2;
        static readonly Vector3 SpawnPosition = new(0f, 0f, -2.4f);

        // Hotspot = the pixel that clicks (in the 48 px icons).
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

            var player = GameObject.Find("Player") ?? new GameObject("Player");
            player.layer = IgnoreRaycastLayer;
            player.transform.SetPositionAndRotation(SpawnPosition, Quaternion.LookRotation(Vector3.forward));

            if (!player.TryGetComponent(out CharacterController body))
                body = player.AddComponent<CharacterController>();
            body.height = 1.75f;
            body.radius = 0.28f;
            body.center = new Vector3(0f, 0.875f, 0f);
            body.stepOffset = 0.25f;
            body.slopeLimit = 45f;
            body.skinWidth = 0.02f;

            var head = player.transform.Find("Head") ?? new GameObject("Head").transform;
            head.SetParent(player.transform, false);
            head.localPosition = new Vector3(0f, 1.65f, 0f);
            head.localRotation = Quaternion.Euler(12f, 0f, 0f);

            camera.transform.SetParent(head, false);
            camera.transform.localPosition = Vector3.zero;
            camera.transform.localRotation = Quaternion.identity;
            if (camera.TryGetComponent(out DevFlyCamera fly))
                Object.DestroyImmediate(fly);

            if (!player.TryGetComponent(out PlayerMotor motor))
                motor = player.AddComponent<PlayerMotor>();
            SetField(motor, "head", head);

            if (!player.TryGetComponent(out DesktopPlayerInput input))
                input = player.AddComponent<DesktopPlayerInput>();
            SetField(input, "motor", motor);
            SetField(input, "actions", AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath));

            var ui = GameObject.Find("HoverTooltip") ?? new GameObject("HoverTooltip");
            if (!ui.TryGetComponent(out HoverTooltip tooltip))
                tooltip = ui.AddComponent<HoverTooltip>();

            if (!player.TryGetComponent(out CursorController cursor))
                cursor = player.AddComponent<CursorController>();
            SetField(cursor, "viewCamera", camera);
            SetField(cursor, "player", input);
            SetField(cursor, "tooltip", tooltip);
            SetCursorStyles(cursor);

            EditorSceneManager.MarkSceneDirty(player.scene);
            Debug.Log("[Player] Player set up: WASD move, Shift sprint, C crouch, Space jump, hold right mouse to look.");
        }

        static void SetCursorStyles(CursorController cursor)
        {
            var so = new SerializedObject(cursor);
            var styles = so.FindProperty("styles");
            styles.arraySize = Cursors.Length;
            for (var i = 0; i < Cursors.Length; i++)
            {
                var element = styles.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("kind").enumValueIndex = (int)Cursors[i].kind;
                element.FindPropertyRelative("texture").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<Texture2D>(UiTextureImport.CursorFolder + Cursors[i].file);
                element.FindPropertyRelative("hotspot").vector2Value = Cursors[i].hotspot;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetField(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
