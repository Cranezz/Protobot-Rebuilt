using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Protobot.Editor
{
    /// <summary>
    /// Helpers for testing in Play mode from scripts (e.g. through the Unity MCP): run frames, put the pointer
    /// somewhere, take screenshots. The app's own types (parts, tools) live in Assembly-CSharp, which this
    /// assembly can't see: call those from the test script itself.
    /// </summary>
    public static class Playtest
    {
        /// <summary>
        /// Enters Play mode so it keeps running while the editor window isn't focused (a script driving it
        /// from outside). Setting Application.runInBackground in the editor changes the project's player
        /// setting, so it's put back when Play mode ends (kept in EditorPrefs, which survive an editor restart).
        /// </summary>
        public static void Play()
        {
            if (!EditorPrefs.GetBool(RestoreKey, false))
            {
                EditorPrefs.SetBool(PreviousKey, PlayerSettings.runInBackground);
                EditorPrefs.SetBool(RestoreKey, true);
            }
            // Both: the player setting for the game, and the editor's own flag so the transition into Play
            // mode doesn't stall while the editor window is in the background.
            PlayerSettings.runInBackground = true;
            Application.runInBackground = true;
            EditorApplication.EnterPlaymode();
        }

        const string RestoreKey = "Protobot.Playtest.RestoreRunInBackground";
        const string PreviousKey = "Protobot.Playtest.RunInBackground";

        // Entering Play mode reloads scripts, so the restore is hooked up again on every load.
        [InitializeOnLoadMethod]
        static void RestoreAfterPlay()
        {
            // Unity writes the play session's value back after this event, so restore a moment later.
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredEditMode)
                    EditorApplication.delayCall += RestoreRunInBackground;
            };
            // Scripts reloading right after Play mode ends drops that delayed call: finish it on the next load.
            EditorApplication.delayCall += RestoreRunInBackground;
        }

        /// <summary>Puts the project's "Run In Background" setting back to what it was before <see cref="Play"/>.</summary>
        public static void RestoreRunInBackground()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !EditorPrefs.GetBool(RestoreKey, false))
                return;
            EditorPrefs.DeleteKey(RestoreKey);
            PlayerSettings.runInBackground = EditorPrefs.GetBool(PreviousKey, false);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Runs the game <paramref name="count"/> frames (pausing it in between). When the editor window isn't
        /// on screen it stops running Play mode frames by itself; this steps them. Returns the frame number.
        /// Stepped frames don't process queued input events: set state with <see cref="PointAt"/>.
        /// </summary>
        public static int Frames(int count)
        {
            EditorApplication.isPaused = true;
            for (var i = 0; i < count; i++)
                EditorApplication.Step();
            return Time.frameCount;
        }

        /// <summary>Moves the pointer to a screen position (applied immediately, also in stepped frames).</summary>
        public static void PointAt(Vector2 screenPosition) =>
            InputState.Change(Mouse.current.position, screenPosition);

        /// <summary>Moves the pointer over a point in the world, seen by the main camera.</summary>
        public static void PointAt(Vector3 worldPoint)
        {
            var screen = Camera.main.WorldToScreenPoint(worldPoint);
            PointAt(new Vector2(screen.x, screen.y));
        }

        /// <summary>
        /// Saves what the player sees (the game view with its UI) as a PNG at the end of this frame, in
        /// Assets/Screenshots (git-ignored). Returns the path. Delete the folder after testing.
        /// </summary>
        public static string Screenshot(string name)
        {
            System.IO.Directory.CreateDirectory("Assets/Screenshots");
            var path = "Assets/Screenshots/" + name + ".png";
            ScreenCapture.CaptureScreenshot(path);
            return path;
        }
    }
}
