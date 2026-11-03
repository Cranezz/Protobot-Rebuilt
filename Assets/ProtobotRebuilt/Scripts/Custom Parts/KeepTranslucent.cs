using UnityEngine;

namespace Protobot.Custom {
    // V2: polycarbonate stays see-through. Colors are set without alpha in several places (placing, painting,
    // loading a build), which would make it solid: this puts its alpha back when that happens.
    public class KeepTranslucent : MonoBehaviour {
        public float alpha = 0.42f;
        private Renderer target;

        private void Awake() => target = GetComponent<Renderer>();

        private void LateUpdate() {
            if (target == null) return;
            var material = target.sharedMaterial;
            if (material == null || Mathf.Approximately(material.color.a, alpha)) return;
            var color = target.material.color;
            color.a = alpha;
            target.material.color = color;
        }
    }
}
