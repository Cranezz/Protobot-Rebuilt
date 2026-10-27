using UnityEngine;
using EPOOutline;
using Protobot.Rendering;

namespace Protobot.Outlining {
    // V2: outlines are drawn by the URP OutlineFeature (Easy Performant Outline's built-in path doesn't run in
    // URP). Same calls and colors as before; the layer and fill arguments are kept for the old callers. Objects
    // that carry their own Outlinable (the hole faces) are switched on and off through it, as before, and drawn
    // by OutlinableBridge.
    public static class Outliner {
        public static void EnableOutline(this GameObject obj, int colorIndex, int layer = 0, float fillAlpha = 0.1f) {
            var color = OutlineSettings.GetColor(colorIndex);
            if (obj.TryGetComponent(out Outlinable outlinable)) {
                outlinable.enabled = true;
                outlinable.OutlineParameters.Color = color;
                return;
            }
            foreach (var renderer in obj.GetComponentsInChildren<Renderer>(true))
                OutlineFeature.SetOutline(renderer, true, color);
        }

        public static void DisableOutline(this GameObject obj) {
            if (obj.TryGetComponent(out Outlinable outlinable)) {
                outlinable.enabled = false;
                return;
            }
            foreach (var renderer in obj.GetComponentsInChildren<Renderer>(true))
                OutlineFeature.SetOutline(renderer, false);
        }
    }
}
