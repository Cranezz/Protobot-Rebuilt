using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Tools {
    public class TransformToolOptions : MonoBehaviour {
        [SerializeField] private MatchObjectLinkTransform linkMatch;
        [SerializeField] private Transform transformToolCanvas;
        // V2: snapping starts on and is remembered (the original started every launch with it off).
        [SerializeField] private Toggle snappingToggle;
        private const string SnappingKey = "TransformSnapping";

        // The options menu starts hidden, so the saved choice is applied at launch, not when it first opens.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LoadSnapping() {
            var snapping = PlayerPrefs.GetInt(SnappingKey, 1) == 1;
            PositionTool.snapping = snapping;
            RotateRing.snapping = snapping;
        }

        private void Awake() {
            if (snappingToggle != null)
                snappingToggle.SetIsOnWithoutNotify(PositionTool.snapping);
        }

        public void OrientGlobal() {
            linkMatch.matchRotation = false;
            transformToolCanvas.eulerAngles = Vector3.zero;
        }

        public void OrientLocal() {
            linkMatch.matchRotation = true;
        }

        public void SetSnapping(bool value) {
            PositionTool.snapping = value;
            RotateRing.snapping = value;
            PlayerPrefs.SetInt(SnappingKey, value ? 1 : 0);
        }
    }
}
