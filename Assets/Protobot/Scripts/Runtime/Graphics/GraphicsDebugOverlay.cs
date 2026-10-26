using UnityEngine;
using UnityEngine.InputSystem;

namespace Protobot.GraphicsQuality
{
    /// <summary>
    /// Temporary developer overlay until the real settings menu exists.
    /// F3 shows/hides it, F1/F2 step down/up through the presets.
    /// </summary>
    public class GraphicsDebugOverlay : MonoBehaviour
    {
        [SerializeField] bool visible;

        float smoothedFrameTime;
        GUIStyle style;

        void Update()
        {
            smoothedFrameTime = Mathf.Lerp(smoothedFrameTime, Time.unscaledDeltaTime, 0.05f);

            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.f3Key.wasPressedThisFrame)
                visible = !visible;

            if (keyboard.f1Key.wasPressedThisFrame)
                StepPreset(-1);

            if (keyboard.f2Key.wasPressedThisFrame)
                StepPreset(+1);
        }

        static void StepPreset(int direction)
        {
            var current = GraphicsSettingsManager.Current;
            if (current == null)
                return;

            // From Custom, stepping starts at the middle of the range.
            var index = current.preset == GraphicsPreset.Custom ? (int)GraphicsPreset.Medium : (int)current.preset + direction;
            index = Mathf.Clamp(index, (int)GraphicsPreset.VeryLow, (int)GraphicsPreset.Extreme);
            GraphicsSettingsManager.SetPreset((GraphicsPreset)index);
        }

        void OnGUI()
        {
            if (!visible)
                return;

            style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, richText = true };

            var s = GraphicsSettingsManager.Current;
            var ms = smoothedFrameTime * 1000f;
            var fps = smoothedFrameTime > 0f ? 1f / smoothedFrameTime : 0f;

            var text = $"<b>{fps:0} FPS</b>  ({ms:0.0} ms)\n" +
                       $"{SystemInfo.graphicsDeviceName}\n" +
                       $"{Screen.width}x{Screen.height}\n";

            if (s != null)
            {
                text += $"<b>Preset: {s.preset}</b>\n" +
                        $"Render scale {s.renderScale:0.##}x · AA {s.antiAliasing}\n" +
                        $"Shadows {s.shadows} · AO {s.ambientOcclusion}\n" +
                        $"Post {(s.postProcessing ? "on" : "off")} · HDR {(s.hdr ? "on" : "off")} · Textures {s.textures}\n";
            }

            text += "<color=#aaa>F1/F2 preset · F3 hide</color>";

            GUI.Box(new Rect(10, 10, 330, 150), text, style);
        }
    }
}
