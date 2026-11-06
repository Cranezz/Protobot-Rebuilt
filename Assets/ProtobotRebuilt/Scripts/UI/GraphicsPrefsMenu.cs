using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Protobot.GraphicsQuality;

namespace Protobot.UI {
    // V2: the Graphics page of the Preferences window. Each row shows one graphics option; clicking its value steps
    // to the next choice, the reset arrow puts back the recommended value. Settings are applied and saved by
    // GraphicsSettingsManager (Very Low to Extreme presets, changed options show the preset as "Custom").
    public class GraphicsPrefsMenu : MonoBehaviour {
        public enum Option { Quality, Shadows, AntiAliasing, AmbientOcclusion, RenderScale, WindowMode, VSync, FrameLimit }

        [Serializable]
        public class Row {
            public Option option;
            public Text valueText;
            public Button valueButton;
            public Button resetButton;
        }

        public List<Row> rows = new List<Row>();

        private static readonly float[] RenderScales = { 0.5f, 0.7f, 0.85f, 1f, 1.25f, 1.5f, 2f };
        private static readonly int[] FrameLimits = { 0, 30, 60, 120, 144, 240 };
        private static readonly FullScreenMode[] WindowModes =
            { FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen, FullScreenMode.Windowed };

        void Awake() {
            foreach (var row in rows) {
                var option = row.option;
                row.valueButton.onClick.AddListener(() => Step(option));
                row.resetButton.onClick.AddListener(() => Reset(option));
            }
        }

        void OnEnable() {
            GraphicsSettingsManager.Changed += OnSettingsChanged;
            Refresh();
            // V2: the graphics settings have their own window now (Edit > Graphics…): this tab opens it, closes
            // Preferences and goes back to the General tab for next time.
            if (Application.isPlaying)
                StartCoroutine(OpenWindow());
        }

        private System.Collections.IEnumerator OpenWindow() {
            yield return null;  // after the tab toggle has finished switching pages
            var root = transform.root;
            Protobot.Interface.GraphicsWindow.Open();
            var close = root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Close Button");
            if (close != null) close.onClick.Invoke();
            // Last: it hides this page (and stops this coroutine).
            var general = root.GetComponentsInChildren<Toggle>(true).FirstOrDefault(t => t.name == "General Toggle");
            if (general != null) general.isOn = true;
        }

        void OnDisable() {
            GraphicsSettingsManager.Changed -= OnSettingsChanged;
        }

        private void OnSettingsChanged(GraphicsSettingsData settings) => Refresh();

        public void Refresh() {
            var settings = GraphicsSettingsManager.Current;
            if (settings == null)
                return;
            foreach (var row in rows)
                row.valueText.text = ValueName(row.option, settings);
        }

        private static void Step(Option option) {
            var current = GraphicsSettingsManager.Current;
            if (current == null)
                return;

            if (option == Option.Quality) {
                var preset = current.preset == GraphicsPreset.Custom ? GraphicsPreset.Medium : current.preset;
                GraphicsSettingsManager.SetPreset(preset == GraphicsPreset.Extreme ? GraphicsPreset.VeryLow : preset + 1);
                return;
            }

            var settings = current.Clone();
            switch (option) {
                case Option.Shadows: settings.shadows = Next(settings.shadows); break;
                case Option.AntiAliasing: settings.antiAliasing = Next(settings.antiAliasing); break;
                case Option.AmbientOcclusion: settings.ambientOcclusion = Next(settings.ambientOcclusion); break;
                case Option.RenderScale: settings.renderScale = Next(RenderScales, settings.renderScale); break;
                case Option.WindowMode: settings.windowMode = Next(WindowModes, settings.windowMode); break;
                case Option.VSync: settings.vSync = !settings.vSync; break;
                case Option.FrameLimit: settings.frameRateLimit = Next(FrameLimits, settings.frameRateLimit); break;
            }
            GraphicsSettingsManager.Apply(settings);
        }

        private static void Reset(Option option) {
            var current = GraphicsSettingsManager.Current;
            if (current == null)
                return;

            var recommended = GraphicsHardwareDetector.RecommendPreset();
            if (option == Option.Quality) {
                GraphicsSettingsManager.SetPreset(recommended);
                return;
            }

            // The quality options go back to what the recommended preset uses; the display ones to the defaults.
            var defaults = new GraphicsSettingsData();
            defaults.SetQualityFromPreset(recommended);
            var settings = current.Clone();
            switch (option) {
                case Option.Shadows: settings.shadows = defaults.shadows; break;
                case Option.AntiAliasing: settings.antiAliasing = defaults.antiAliasing; break;
                case Option.AmbientOcclusion: settings.ambientOcclusion = defaults.ambientOcclusion; break;
                case Option.RenderScale: settings.renderScale = defaults.renderScale; break;
                case Option.WindowMode: settings.windowMode = defaults.windowMode; break;
                case Option.VSync: settings.vSync = defaults.vSync; break;
                case Option.FrameLimit: settings.frameRateLimit = defaults.frameRateLimit; break;
            }
            GraphicsSettingsManager.Apply(settings);
        }

        private static string ValueName(Option option, GraphicsSettingsData settings) => option switch {
            Option.Quality => settings.preset == GraphicsPreset.VeryLow ? "Very Low" : settings.preset.ToString(),
            Option.Shadows => settings.shadows.ToString(),
            Option.AntiAliasing => settings.antiAliasing switch {
                AntiAliasing.MSAA2x => "MSAA 2x",
                AntiAliasing.MSAA4x => "MSAA 4x",
                AntiAliasing.MSAA8x => "MSAA 8x",
                var aa => aa.ToString()
            },
            Option.AmbientOcclusion => settings.ambientOcclusion.ToString(),
            Option.RenderScale => Mathf.RoundToInt(settings.renderScale * 100f) + "%",
            Option.WindowMode => settings.windowMode switch {
                FullScreenMode.ExclusiveFullScreen => "Fullscreen",
                FullScreenMode.Windowed => "Windowed",
                _ => "Borderless"
            },
            Option.VSync => settings.vSync ? "On" : "Off",
            Option.FrameLimit => settings.frameRateLimit <= 0 ? "Unlimited" : settings.frameRateLimit + " FPS",
            _ => ""
        };

        private static T Next<T>(T value) where T : Enum {
            var values = (T[])Enum.GetValues(typeof(T));
            var index = Array.IndexOf(values, value);
            return values[(index + 1) % values.Length];
        }

        // The choice after the one closest to the current value (so a value from an older version still steps on).
        private static T Next<T>(T[] choices, T value) {
            var index = Array.IndexOf(choices, value);
            if (index < 0)
                index = Closest(choices, value);
            return choices[(index + 1) % choices.Length];
        }

        private static int Closest<T>(T[] choices, T value) {
            var best = 0;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < choices.Length; i++) {
                var distance = Mathf.Abs(Convert.ToSingle(choices[i]) - Convert.ToSingle(value));
                if (distance < bestDistance) {
                    best = i;
                    bestDistance = distance;
                }
            }
            return best;
        }
    }
}
