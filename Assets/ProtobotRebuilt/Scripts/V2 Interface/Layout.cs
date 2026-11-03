using UnityEngine;

namespace Protobot.Interface {
    // V2: where the original's fixed UI sits, in the new interface's canvas units, so new panels line up with it.
    public static class Layout {
        private static RectTransform topBar;
        private static int measuredFrame = -1;
        private static float topBarBottom = 34f;

        /// <summary>Distance from the top of the screen to the bottom of the original top bar.</summary>
        public static float TopBarBottom {
            get {
                // The new top bar (the original's is hidden behind it).
                if (TopBar.Instance != null)
                    return TopBar.Height;
                if (measuredFrame == Time.frameCount)
                    return topBarBottom;
                measuredFrame = Time.frameCount;
                if (topBar == null) {
                    var canvas = GameObject.Find("Build Canvas");
                    topBar = canvas != null ? canvas.transform.Find("Top Bar") as RectTransform : null;
                }
                if (topBar == null || Interface.Root == null)
                    return topBarBottom;
                var corners = new Vector3[4];
                topBar.GetWorldCorners(corners);  // overlay canvas: world = screen pixels
                var scale = Interface.Root.lossyScale.y;
                if (scale > 0f)
                    topBarBottom = (Screen.height - corners[0].y) / scale;
                return topBarBottom;
            }
        }

        /// <summary>A screen point (pixels) in the new interface's canvas units, from its bottom-left corner.</summary>
        public static Vector2 FromScreen(Vector2 screen) {
            var scale = Interface.Root != null ? Interface.Root.lossyScale.x : 1f;
            return screen / Mathf.Max(scale, 0.0001f);
        }
    }
}
