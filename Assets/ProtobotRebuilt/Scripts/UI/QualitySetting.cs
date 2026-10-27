using UnityEngine;
using UnityEngine.UI;
using Protobot.GraphicsQuality;

namespace Protobot.UI {
    // V2: steps through the graphics presets (Very Low to Extreme, GraphicsSettingsManager) instead of Unity's
    // quality levels. The int passed around is the preset's index.
    public class QualitySetting : MonoBehaviour {
        [SerializeField] private Text qualityText;
        [SerializeField] private IntEvent OnSetQuality;

        private const int Highest = (int)GraphicsPreset.Extreme;

        private static int CurrentQualityLevel {
            get {
                var current = GraphicsSettingsManager.Current;
                if (current == null || current.preset == GraphicsPreset.Custom)
                    return (int)GraphicsPreset.Medium;
                return (int)current.preset;
            }
        }

        void Start() {
            SetQualityText(CurrentQualityLevel);
        }

        public void SetQualityText(int qualityLevel) {
            qualityText.text = PresetName((GraphicsPreset)Mathf.Clamp(qualityLevel, 0, Highest));
        }

        public void SetQuality(int newQualityLevel) {
            GraphicsSettingsManager.SetPreset((GraphicsPreset)Mathf.Clamp(newQualityLevel, 0, Highest));
            OnSetQuality?.Invoke(CurrentQualityLevel);
        }

        public void IncQuality() => SetQuality(CurrentQualityLevel + 1);

        public void DecQuality() => SetQuality(CurrentQualityLevel - 1);

        private static string PresetName(GraphicsPreset preset) => preset switch {
            GraphicsPreset.VeryLow => "Very Low",
            _ => preset.ToString()
        };
    }
}
