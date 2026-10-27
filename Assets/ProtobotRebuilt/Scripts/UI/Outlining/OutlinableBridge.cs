using System.Collections.Generic;
using EPOOutline;
using Protobot.Rendering;
using UnityEngine;

namespace Protobot.Outlining {
    // V2: Easy Performant Outline's renderer doesn't run in URP. Objects that use its Outlinable directly (the
    // hovered and selected hole faces) are outlined by the URP OutlineFeature instead, in the Outlinable's color.
    // Its own list of outlinables follows renderer visibility events the built-in pipeline sent, so the scene is
    // searched instead (every couple of seconds: they're few and made with the scene).
    public class OutlinableBridge : MonoBehaviour {
        private const float SearchInterval = 2f;
        private Outlinable[] active = new Outlinable[0];
        private float nextSearch;
        private readonly HashSet<Renderer> outlined = new HashSet<Renderer>();
        private readonly HashSet<Renderer> stillOutlined = new HashSet<Renderer>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create() {
            var bridge = new GameObject("Outlinable Bridge") { hideFlags = HideFlags.HideInHierarchy };
            bridge.AddComponent<OutlinableBridge>();
            DontDestroyOnLoad(bridge);
        }

        private void LateUpdate() {
            if (Time.unscaledTime >= nextSearch) {
                active = FindObjectsByType<Outlinable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                nextSearch = Time.unscaledTime + SearchInterval;
            }
            stillOutlined.Clear();
            foreach (var outlinable in active) {
                if (outlinable == null || !outlinable.isActiveAndEnabled)
                    continue;
                var parameters = outlinable.RenderStyle == RenderStyle.Single ? outlinable.OutlineParameters : outlinable.FrontParameters;
                if (!parameters.Enabled)
                    continue;
                foreach (var target in outlinable.OutlineTargets) {
                    var renderer = target.Renderer;
                    if (renderer == null)
                        continue;
                    OutlineFeature.SetOutline(renderer, true, parameters.Color);
                    stillOutlined.Add(renderer);
                }
            }

            foreach (var renderer in outlined)
                if (renderer != null && !stillOutlined.Contains(renderer))
                    OutlineFeature.SetOutline(renderer, false);
            outlined.Clear();
            outlined.UnionWith(stillOutlined);
        }
    }
}
