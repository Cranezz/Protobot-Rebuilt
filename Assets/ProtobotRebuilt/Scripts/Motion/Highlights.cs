using System.Collections.Generic;
using Protobot.Rendering;
using UnityEngine;

namespace Protobot.Motion {
    // V2: outlines that stay on (grounded parts in a faint red, motors geared to the ones being controlled in red)
    // under the selection and hover outlines: a part outlined by those shows their color, and gets this one back
    // when they let go. "Strong" ones (the motors) show over the selection's outline.
    public class Highlights : MonoBehaviour {
        public static readonly Color Grounded = new(0.62f, 0.24f, 0.24f, 1f);
        public static readonly Color Unpaired = new(0.95f, 0.2f, 0.18f, 1f);

        private static Highlights host;
        private readonly Dictionary<string, (HashSet<GameObject> objects, Color color, bool strong)> requests = new();
        private readonly Dictionary<Renderer, Color> shown = new();         // what this put on each renderer
        private readonly Dictionary<Renderer, Color> covered = new();       // the outline a strong one covered
        private static readonly MaterialPropertyBlock Block = new();

        private static Highlights Host {
            get {
                if (host == null) host = new GameObject("Highlights").AddComponent<Highlights>();
                return host;
            }
        }

        public static void Set(string owner, IEnumerable<GameObject> objects, Color color, bool strong = false) =>
            Host.requests[owner] = (new HashSet<GameObject>(objects), color, strong);

        public static void Clear(string owner) {
            if (host != null) host.requests.Remove(owner);
        }

        private static Color Current(Renderer renderer) {
            if ((renderer.renderingLayerMask & OutlineFeature.Layer) == 0) return Color.clear;
            renderer.GetPropertyBlock(Block);
            return Block.GetColor(OutlineFeature.ObjectColorId);
        }

        private static bool Same(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.01f;

        // Reused every frame (no garbage while outlines are on).
        private readonly Dictionary<Renderer, (Color color, bool strong)> wanted = new();
        private readonly List<Renderer> found = new();
        private readonly List<Renderer> gone = new();

        private void LateUpdate() {
            if (requests.Count == 0 && shown.Count == 0) return;
            // What each renderer should show from here (the strongest request wins).
            wanted.Clear();
            foreach (var request in requests.Values)
                foreach (var obj in request.objects) {
                    if (obj == null || obj.IsDeleted()) continue;
                    obj.GetComponentsInChildren(found);
                    foreach (var renderer in found) {
                        if (renderer is not MeshRenderer || !renderer.enabled) continue;
                        if (wanted.TryGetValue(renderer, out var have) && (have.strong || !request.strong)) continue;
                        wanted[renderer] = (request.color, request.strong);
                    }
                }

            // Let go of what isn't wanted any more.
            gone.Clear();
            foreach (var pair in shown)
                if (pair.Key == null) gone.Add(pair.Key);
                else if (!wanted.ContainsKey(pair.Key)) {
                    if (Same(Current(pair.Key), pair.Value)) {
                        if (covered.TryGetValue(pair.Key, out var under)) OutlineFeature.SetOutline(pair.Key, true, under);
                        else OutlineFeature.SetOutline(pair.Key, false);
                    }
                    gone.Add(pair.Key);
                }
            foreach (var renderer in gone) {
                shown.Remove(renderer);
                covered.Remove(renderer);
            }

            foreach (var pair in wanted) {
                var renderer = pair.Key;
                var current = Current(renderer);
                var mine = shown.TryGetValue(renderer, out var last) && Same(current, last);
                if (mine && Same(current, pair.Value.color)) continue;
                if (current.a > 0.5f && !mine) {
                    // Another outline (selection, hover) is on it.
                    if (!pair.Value.strong) {
                        shown.Remove(renderer);
                        continue;
                    }
                    covered[renderer] = current;
                }
                else if (current.a <= 0.5f) covered.Remove(renderer);
                OutlineFeature.SetOutline(renderer, true, pair.Value.color);
                shown[renderer] = pair.Value.color;
            }
        }
    }
}
