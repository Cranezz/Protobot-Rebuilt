using Protobot.Parts;
using Protobot.Player;
using Protobot.Tools;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Protobot.Editor
{
    /// <summary>
    /// Helpers for testing in Play mode from scripts (e.g. through the Unity MCP): stand the player somewhere,
    /// aim at a point, spawn parts, and press mouse buttons and keys through the Input System, so the game
    /// sees the same input a real player would give it.
    /// </summary>
    public static class Playtest
    {
        const string CatalogPath = "Assets/Protobot/Parts/PartCatalog.asset";

        static MouseState mouse;
        static readonly System.Collections.Generic.HashSet<Key> keysDown = new();

        /// <summary>
        /// Enters Play mode so it keeps running while the editor window isn't focused (a script driving it
        /// from outside). Setting Application.runInBackground in the editor changes the project's player
        /// setting, so it's put back when Play mode ends.
        /// </summary>
        public static void Play()
        {
            if (!SessionState.GetBool(RestoreKey, false))
            {
                SessionState.SetBool(PreviousKey, PlayerSettings.runInBackground);
                SessionState.SetBool(RestoreKey, true);
            }
            PlayerSettings.runInBackground = true;
            EditorApplication.EnterPlaymode();
        }

        const string RestoreKey = "Protobot.Playtest.RestoreRunInBackground";
        const string PreviousKey = "Protobot.Playtest.RunInBackground";

        // Entering Play mode reloads scripts, so the restore is hooked up again on every load.
        [InitializeOnLoadMethod]
        static void RestoreAfterPlay() => EditorApplication.playModeStateChanged += change =>
        {
            if (change != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(RestoreKey, false))
                return;
            SessionState.EraseBool(RestoreKey);
            var previous = SessionState.GetBool(PreviousKey, false);
            // Unity writes the play session's value back after this event, so restore a moment later.
            EditorApplication.delayCall += () =>
            {
                PlayerSettings.runInBackground = previous;
                AssetDatabase.SaveAssets();
            };
        };

        public static PlayerMotor Motor => Object.FindFirstObjectByType<PlayerMotor>();
        public static ToolBelt Belt => Object.FindFirstObjectByType<ToolBelt>();

        /// <summary>Stands the player at <paramref name="feet"/> looking at <paramref name="target"/>.</summary>
        public static void StandAndLook(Vector3 feet, Vector3 target)
        {
            var motor = Motor;
            var eye = feet + Vector3.up * (motor.Head.position.y - motor.transform.position.y);
            var dir = (target - eye).normalized;
            var yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            var pitch = -Mathf.Asin(dir.y) * Mathf.Rad2Deg;
            motor.Teleport(feet, yaw, pitch);
        }

        /// <summary>
        /// Lays a part from the catalog flat on a surface: its longest side along <paramref name="along"/>, its
        /// thinnest side up, centred on <paramref name="centre"/> with its underside at the centre's height.
        /// <paramref name="flip"/> turns it upside down (e.g. a C-channel's open side down).
        /// </summary>
        public static GameObject SpawnLying(string id, Vector3 centre, Vector3 along, bool flip = false)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PartCatalog>(CatalogPath);
            var entry = catalog.parts.Find(p => p.id == id);
            if (entry == null || entry.prefab == null)
                return null;
            var go = Object.Instantiate(entry.prefab);
            go.name = entry.displayName;
            var bounds = PartShape.LocalBounds(go);
            var rotation = PartSpawner.LyingRotation(go, along.normalized);
            if (flip)
                rotation = Quaternion.AngleAxis(180f, along.normalized) * rotation;
            var size = rotation * bounds.size;
            var underside = rotation * bounds.center - Vector3.up * (Mathf.Abs(size.y) / 2f);
            var position = centre - new Vector3(underside.x, underside.y, underside.z) + Vector3.up * 0.001f;
            var body = go.GetComponent<Rigidbody>();
            go.transform.SetPositionAndRotation(position, rotation);
            body.position = position;
            body.rotation = rotation;
            Physics.SyncTransforms();
            return go;
        }

        public static void Equip(int index) => Belt.Equip(Belt.Tools[index]);

        public static void Mouse(MouseButton button, bool down)
        {
            mouse = mouse.WithButton(button, down);
            mouse.scroll = Vector2.zero;
            mouse.delta = Vector2.zero;
            InputSystem.QueueStateEvent(UnityEngine.InputSystem.Mouse.current, mouse);
        }

        public static void Scroll(float notches)
        {
            mouse.scroll = new Vector2(0f, notches * 120f);
            mouse.delta = Vector2.zero;
            InputSystem.QueueStateEvent(UnityEngine.InputSystem.Mouse.current, mouse);
        }

        /// <summary>Puts the (unlocked) pointer at a screen position, e.g. over a menu button.</summary>
        public static void PointAt(Vector2 screenPosition)
        {
            mouse.position = screenPosition;
            mouse.delta = Vector2.zero;
            mouse.scroll = Vector2.zero;
            InputSystem.QueueStateEvent(UnityEngine.InputSystem.Mouse.current, mouse);
        }

        /// <summary>Screen position of a UI element's centre (for <see cref="PointAt"/>).</summary>
        public static Vector2 ScreenPositionOf(string path)
        {
            var go = GameObject.Find(path);
            var rect = (RectTransform)go.transform;
            return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        }

        public static void MoveMouse(Vector2 delta)
        {
            mouse.delta = delta;
            mouse.scroll = Vector2.zero;
            InputSystem.QueueStateEvent(UnityEngine.InputSystem.Mouse.current, mouse);
        }

        public static void Key(Key key, bool down)
        {
            if (down)
                keysDown.Add(key);
            else
                keysDown.Remove(key);
            var state = new KeyboardState();
            foreach (var k in keysDown)
                state.Press(k);
            InputSystem.QueueStateEvent(Keyboard.current, state);
        }

        public static void ReleaseAll()
        {
            keysDown.Clear();
            mouse = new MouseState();
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            InputSystem.QueueStateEvent(UnityEngine.InputSystem.Mouse.current, mouse);
        }
    }
}
