using System.Collections;
using System.IO;
using UnityEngine;

namespace Protobot.Builds {
    /// <summary>V2 (trailer footage, developer use): records the game view frame by frame at a fixed frame rate
    /// (Time.captureFramerate, so the footage is smooth however slowly the computer runs) as JPGs in a folder, for
    /// ffmpeg to make into video. Does nothing unless started: FrameRecorder.Record(folder, seconds).</summary>
    public class FrameRecorder : MonoBehaviour {
        public static FrameRecorder Current { get; private set; }
        public static int Frame => Current != null ? Current.frame : -1;
        public static bool Recording => Current != null;

        private string folder;
        private int frames, frame, fps;

        public static void Record(string folder, float seconds, int fps = 30) {
            if (Current != null) Destroy(Current.gameObject);
            Directory.CreateDirectory(folder);
            foreach (var old in Directory.GetFiles(folder, "*.jpg")) File.Delete(old);
            var go = new GameObject("Frame Recorder") { hideFlags = HideFlags.HideAndDontSave };
            Current = go.AddComponent<FrameRecorder>();
            Current.folder = folder;
            Current.fps = fps;
            Current.frames = Mathf.RoundToInt(seconds * fps);
            Time.captureFramerate = fps;
        }

        public static void Stop() {
            if (Current != null) Destroy(Current.gameObject);
        }

        // ---- optional camera move and hidden interface (cinematic shots)

        private static bool orbiting;
        private static Vector3 orbitCenter;
        private static Transform orbitFollow;
        private static float orbitRadius, orbitHeight, orbitStart, orbitSpeed, orbitLook, orbitZoom;
        private static readonly System.Collections.Generic.List<Behaviour> pausedDrivers = new();
        private static readonly System.Collections.Generic.List<Canvas> hiddenCanvases = new();
        private float startTime;

        /// <summary>Flies the main camera round a point (or a moving transform) while recording: radius and height in
        /// inches, starting angle and degrees a second; zoom: radius change a second. The camera's own controls pause.</summary>
        public static void Orbit(Vector3 center, Transform follow, float radius, float height, float startDeg, float degPerSec, float lookHeight = 0f, float zoom = 0f) {
            orbiting = true;
            orbitCenter = center;
            orbitFollow = follow;
            orbitRadius = radius;
            orbitHeight = height;
            orbitStart = startDeg;
            orbitSpeed = degPerSec;
            orbitLook = lookHeight;
            orbitZoom = zoom;
            var cam = Camera.main;
            if (cam != null)
                foreach (var b in cam.GetComponents<Behaviour>())
                    if (b is MonoBehaviour && b.enabled && !(b is FrameRecorder) && b.GetType().Namespace != "UnityEngine.Rendering.Universal") {
                        b.enabled = false;
                        pausedDrivers.Add(b);
                    }
        }

        public static void StopOrbit() {
            orbiting = false;
            foreach (var b in pausedDrivers) if (b != null) b.enabled = true;
            pausedDrivers.Clear();
        }

        /// <summary>Hides (or shows again) every screen canvas: footage without the interface.</summary>
        public static void HideInterface(bool hide) {
            if (hide) {
                foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                    if (c.isRootCanvas && c.enabled && c.renderMode != RenderMode.WorldSpace) {
                        c.enabled = false;
                        hiddenCanvases.Add(c);
                    }
            }
            else {
                foreach (var c in hiddenCanvases) if (c != null) c.enabled = true;
                hiddenCanvases.Clear();
            }
        }

        private void LateUpdate() {
            if (!orbiting) return;
            var cam = Camera.main;
            if (cam == null) return;
            var t = Time.time - startTime;
            var center = orbitFollow != null ? orbitFollow.position : orbitCenter;
            var angle = (orbitStart + orbitSpeed * t) * Mathf.Deg2Rad;
            var radius = orbitRadius + orbitZoom * t;
            cam.transform.position = center + new Vector3(Mathf.Sin(angle) * radius, orbitHeight, Mathf.Cos(angle) * radius);
            cam.transform.LookAt(center + Vector3.up * orbitLook);
        }

        private void Start() {
            startTime = Time.time;
            StartCoroutine(Capture());
        }

        private IEnumerator Capture() {
            var end = new WaitForEndOfFrame();
            while (frame < frames) {
                yield return end;
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(folder, frame.ToString("00000") + ".jpg"), texture.EncodeToJPG(93));
                Destroy(texture);
                frame++;
            }
            Destroy(gameObject);
        }

        private void OnDestroy() {
            if (Current == this) {
                Current = null;
                Time.captureFramerate = 0;
                StopOrbit();
                HideInterface(false);
            }
        }
    }
}
