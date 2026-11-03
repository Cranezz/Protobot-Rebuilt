using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

        private static TransformToolOptions instance;

        private void Awake() {
            instance = this;
            if (snappingToggle != null)
                snappingToggle.SetIsOnWithoutNotify(PositionTool.snapping);
        }

        // V2: the same settings in the tool options bar (the right-click menu was hard to find).
        [RuntimeInitializeOnLoadMethod]
        private static void RegisterOptions() => Protobot.Interface.ToolRail.RegisterOptions("Transform", BuildOptions);

        private static void BuildOptions(RectTransform bar) {
            if (instance == null)
                instance = FindFirstObjectByType<TransformToolOptions>(FindObjectsInactive.Include);
            if (instance == null)
                return;
            var local = instance.linkMatch.matchRotation;
            Protobot.Interface.ToolOptions.Segmented(bar, new[] { "Global", "Local" }, local ? 1 : 0, i => {
                if (i == 0) instance.OrientGlobal();
                else instance.OrientLocal();
                Protobot.Interface.ToolOptions.Rebuild();
            });
            Protobot.Interface.ToolOptions.Switch(bar, "Snap", PositionTool.snapping, on => {
                instance.SetSnapping(on);
                if (instance.snappingToggle != null)
                    instance.snappingToggle.SetIsOnWithoutNotify(on);
            });
            // V2: the snap steps are picked from presets (the wheel over one steps through them).
            StepChoice(bar, "Move", MoveSteps, TransformGizmo.MoveStep, x => Protobot.Interface.Inch.Format(x), value => {
                TransformGizmo.MoveStep = value;
                Protobot.Interface.Interface.Say("Moves snap every " + Protobot.Interface.Inch.Format(value));
            }, "Snap step for the arrows. Scroll over it to step through the sizes.");
            StepChoice(bar, "Turn", TurnSteps, TransformGizmo.TurnStep, Degrees, value => {
                TransformGizmo.TurnStep = value;
                Protobot.Interface.Interface.Say("Turns snap every " + Degrees(value));
            }, "Snap step for the rings. Scroll over it to step through the angles.");
        }

        private static readonly float[] MoveSteps = { 1f / 64f, 1f / 32f, 1f / 16f, 1f / 8f, 1f / 4f, 1f / 2f, 1f, 2f };
        private static readonly float[] TurnSteps = { 1f, 5f, 7.5f, 15f, 22.5f, 30f, 45f, 90f };

        private static string Degrees(float value) => value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "°";

        private static void StepChoice(RectTransform bar, string label, float[] values, float current, System.Func<float, string> format, System.Action<float> set, string tip) {
            Protobot.Interface.ToolOptions.Note(bar, label);
            var index = Protobot.Interface.WheelStep.Next(values, current, 0);
            var choice = Protobot.Interface.Popup.Choice(bar, values.Select(format).ToArray(), index, i => {
                set(values[i]);
                Protobot.Interface.ToolOptions.Rebuild();
            }, 76f);
            choice.gameObject.AddComponent<Protobot.Interface.WheelStep>().step = direction => {
                var next = Protobot.Interface.WheelStep.Next(values, current, direction);
                if (Mathf.Abs(values[next] - current) < 0.0001f) return;
                set(values[next]);
                Protobot.Interface.ToolOptions.Rebuild();
            };
            Protobot.Interface.Ui.Tip(choice, label + " step", "Wheel", tip);
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
