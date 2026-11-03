using UnityEngine;
using UnityEngine.EventSystems;

namespace Protobot.Interface {
    // V2: the mouse wheel over a control steps its value: up = the next one, down = the one before (the drill's
    // bits, the Transform tool's snap steps).
    public class WheelStep : MonoBehaviour, IScrollHandler {
        public System.Action<int> step;

        public void OnScroll(PointerEventData e) {
            if (Mathf.Abs(e.scrollDelta.y) > 0.01f) step?.Invoke(e.scrollDelta.y > 0f ? 1 : -1);
        }

        /// <summary>Steps through a list of values: the index of the one nearest the current, moved and clamped.</summary>
        public static int Next(float[] values, float current, int direction) {
            var index = 0;
            for (var i = 1; i < values.Length; i++)
                if (Mathf.Abs(values[i] - current) < Mathf.Abs(values[index] - current)) index = i;
            return Mathf.Clamp(index + direction, 0, values.Length - 1);
        }
    }
}
