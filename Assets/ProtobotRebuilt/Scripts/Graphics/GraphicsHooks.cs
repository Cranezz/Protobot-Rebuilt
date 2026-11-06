using Protobot.GraphicsQuality;
using TMPro;
using UnityEngine;

namespace Protobot.Interface {
    // V2: ties the graphics settings (Protobot.Runtime) to the app: what keeps the full frame rate while idle
    // (the simulation, running motors, the benchmark, camera moves and sliding toasts), and the frame rate counter.
    public static class GraphicsHooks {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Hook() {
            IdleFrameRate.Busy.Add(() => Motion.Sandbox.Running);
            IdleFrameRate.Busy.Add(() => Motion.MotorRunner.Active);
            IdleFrameRate.Busy.Add(() => GraphicsBenchmark.Running);
            IdleFrameRate.Busy.Add(() => Game.SkillsGame.Active);
            IdleFrameRate.Busy.Add(() => DG.Tweening.DOTween.TotalPlayingTweens() > 0);
            if (Interface.Root != null)
                FrameRateCounter.Create(Interface.Root);
        }
    }

    // The "Frame rate counter" setting: FPS and frame time in the bottom-left corner.
    public class FrameRateCounter : MonoBehaviour {
        private TextMeshProUGUI text;
        private float smoothed = 1f / 60f, next;

        public static void Create(RectTransform root) {
            var rect = Ui.Rect("Frame Rate Counter", root);
            rect.Place(Vector2.zero, Vector2.zero, new Vector2(72f, 14f), new Vector2(220f, 22f));
            var counter = rect.gameObject.AddComponent<FrameRateCounter>();
            counter.text = Ui.Text(rect, "Text", "", Theme.FontSmall, Theme.Text, Theme.Medium);
            counter.text.rectTransform.Fill();
            counter.text.outlineWidth = 0.2f;
            counter.text.outlineColor = new Color32(0, 0, 0, 200);
        }

        private void Update() {
            var show = GraphicsSettingsManager.Current != null && GraphicsSettingsManager.Current.showFrameRate;
            text.enabled = show;
            if (!show) return;
            smoothed = Mathf.Lerp(smoothed, Time.unscaledDeltaTime, 0.1f);
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.25f;
            var fps = 1f / Mathf.Max(smoothed, 0.0001f);
            text.color = fps >= 55f ? Theme.Success : fps >= 28f ? Theme.Warning : Theme.Danger;
            text.text = $"{fps:0} FPS  {smoothed * 1000f:0.0} ms" + (GraphicsSettingsManager.IdleLimit > 0 ? "  (idle)" : "");
        }
    }
}
