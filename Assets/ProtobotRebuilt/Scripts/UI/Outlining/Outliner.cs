using UnityEngine;
using Protobot.Rendering;

namespace Protobot.Outlining {
    // V2: outlines are drawn by the URP OutlineFeature (Easy Performant Outline's built-in path doesn't run in
    // URP). Same calls and colors as before; the layer and fill arguments are kept for the old callers. Objects
    // that carry their own FaceOutline (the hole faces; Easy Performant Outline's Outlinable before) are switched on
    // and off through it, as before.
    public static class Outliner {
        public static void EnableOutline(this GameObject obj, int colorIndex, int layer = 0, float fillAlpha = 0.1f) {
            var color = OutlineSettings.GetColor(colorIndex);
            if (obj.TryGetComponent(out FaceOutline outlinable)) {
                outlinable.color = color;
                outlinable.enabled = true;
                outlinable.Apply();
                return;
            }
            foreach (var renderer in obj.GetComponentsInChildren<Renderer>(true))
                OutlineFeature.SetOutline(renderer, true, color);
        }

        public static void DisableOutline(this GameObject obj) {
            if (obj.TryGetComponent(out FaceOutline outlinable)) {
                outlinable.enabled = false;
                return;
            }
            foreach (var renderer in obj.GetComponentsInChildren<Renderer>(true))
                OutlineFeature.SetOutline(renderer, false);
        }
    }
}
