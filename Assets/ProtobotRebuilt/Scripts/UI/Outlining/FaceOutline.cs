using Protobot.Rendering;
using UnityEngine;

namespace Protobot.Outlining {
    /// <summary>V2: an object's own outline (the hovered and selected hole faces), drawn by the URP OutlineFeature in
    /// <see cref="color"/> while the component is enabled. It replaces Easy Performant Outline's Outlinable (a paid
    /// Asset Store package that can't be in a public GPL repository); Outliner switches it on and off as it did that.</summary>
    public class FaceOutline : MonoBehaviour {
        public Color color = Color.yellow;
        public Renderer[] targets = new Renderer[0];

        private void OnEnable() => Apply();

        private void OnDisable() {
            foreach (var target in targets)
                if (target != null) OutlineFeature.SetOutline(target, false);
        }

        /// <summary>Draws the outline in the current color (after a color change).</summary>
        public void Apply() {
            if (!isActiveAndEnabled) return;
            if (targets == null) return;
            foreach (var target in targets)
                if (target != null) OutlineFeature.SetOutline(target, true, color);
        }
    }
}
