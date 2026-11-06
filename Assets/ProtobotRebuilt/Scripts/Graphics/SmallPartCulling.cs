using System.Collections.Generic;
using Protobot.GraphicsQuality;
using UnityEngine;

namespace Protobot.Interface {
    // V2: "Hide tiny far-away parts" (Graphics > Quality > Detail, GraphicsSettingsData.smallPartCulling). Builds are
    // millions of triangles because every part is VEX's full-detail model, and on integrated graphics drawing them is
    // the whole cost. A screw far from the camera is a few pixels but still thousands of triangles: this skips drawing
    // any part smaller on screen than 2, 5 or 10 pixels (Renderer.forceRenderingOff, so nothing else changes: it can
    // still be clicked, it still moves, it's saved). The models are never simplified, and a part comes back in full
    // detail as soon as it's bigger. Off by default; the main camera only.
    public class SmallPartCulling : MonoBehaviour {
        private static readonly float[] Pixels = { 0f, 2f, 5f, 10f };
        private const float RefreshSeconds = 2f;

        private static SmallPartCulling instance;
        private readonly List<Renderer> tracked = new();
        private long version = -1;
        private float nextRefresh;
        private bool active;

        /// <summary>How many parts were skipped last frame (for the graphics window).</summary>
        public static int Hidden { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create() {
            if (instance != null) return;
            var go = new GameObject("Small Part Culling") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SmallPartCulling>();
        }

        // After everything has moved this frame, before it's drawn.
        private void LateUpdate() {
            var settings = GraphicsSettingsManager.Current;
            var level = settings == null ? 0 : Mathf.Clamp(settings.smallPartCulling, 0, Pixels.Length - 1);
            var camera = Camera.main;
            if (level == 0 || camera == null) {
                if (active) ShowAll();
                return;
            }
            active = true;

            if (Motion.Dynamics.Version != version || Time.unscaledTime >= nextRefresh)
                Refresh();

            // Height on screen in pixels of a sphere around the part: diameter * pixelsPerUnit / distance.
            var pixelHeight = camera.pixelHeight;
            var position = camera.transform.position;
            var minimum = Pixels[level];
            float perspective = 0f, ortho = 0f;
            if (camera.orthographic) ortho = pixelHeight / (2f * camera.orthographicSize);
            else perspective = pixelHeight / (2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad));
            var hidden = 0;
            for (var i = tracked.Count - 1; i >= 0; i--) {
                var renderer = tracked[i];
                if (renderer == null) {
                    tracked.RemoveAt(i);
                    continue;
                }
                var bounds = renderer.bounds;
                var diameter = bounds.extents.magnitude * 2f;
                float size;
                if (camera.orthographic) size = diameter * ortho;
                else {
                    var distance = Vector3.Distance(position, bounds.center);
                    size = distance <= diameter ? float.MaxValue : diameter * perspective / distance;
                }
                var hide = size < minimum && !Outlined(renderer);
                if (renderer.forceRenderingOff != hide) renderer.forceRenderingOff = hide;
                if (hide) hidden++;
            }
            Hidden = hidden;
        }

        // A part being outlined (selected, hovered, in a tool) is always drawn.
        private static bool Outlined(Renderer renderer) => (renderer.renderingLayerMask & Rendering.OutlineFeature.Layer) != 0;

        private void Refresh() {
            version = Motion.Dynamics.Version;
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            var seen = new HashSet<Renderer>(tracked);
            foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)) {
                // Parts (Default/Parts), the field, the simulation's copies: not the interface, grid or tool previews.
                var layer = renderer.gameObject.layer;
                if (layer != 0 && layer != 13 && layer != 16 && layer != 17) continue;
                if (seen.Add(renderer)) tracked.Add(renderer);
            }
        }

        private void ShowAll() {
            foreach (var renderer in tracked)
                if (renderer != null && renderer.forceRenderingOff)
                    renderer.forceRenderingOff = false;
            tracked.Clear();
            version = -1;
            active = false;
            Hidden = 0;
        }

        private void OnDestroy() {
            ShowAll();
            if (instance == this) instance = null;
        }
    }
}
