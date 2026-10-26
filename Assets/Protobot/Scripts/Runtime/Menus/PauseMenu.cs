using System;
using System.Collections.Generic;
using Protobot.Core;
using Protobot.GraphicsQuality;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using ShadowQuality = Protobot.GraphicsQuality.ShadowQuality;

namespace Protobot.Menus
{
    /// <summary>
    /// Esc menu: Resume, Settings, Quit. Pauses the game (physics stops) while open.
    /// Settings has a Graphics tab (preset and each individual option, applied instantly and saved)
    /// and a Controls tab (mouse sensitivity, invert Y, field of view).
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        [SerializeField] UiTheme theme;

        GameObject root;
        GameObject mainPage;
        GameObject settingsPage;
        GameObject graphicsTab;
        GameObject controlsTab;
        Button graphicsTabButton;
        Button controlsTabButton;
        readonly List<Action> refreshers = new();
        bool refreshing;

        public bool IsOpen => root != null && root.activeSelf;

        void Awake()
        {
            UiFactory.Canvas(gameObject, 30);
            Build();
            root.SetActive(false);
        }

        void OnEnable() => GraphicsSettingsManager.Changed += OnGraphicsChanged;

        void OnDisable()
        {
            GraphicsSettingsManager.Changed -= OnGraphicsChanged;
            if (IsOpen)
                Close();
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame || MenuState.ChangedFrame == Time.frameCount)
                return;

            if (IsOpen)
            {
                if (settingsPage.activeSelf)
                    ShowMain();
                else
                    Close();
            }
            else if (!MenuState.AnyOpen)
            {
                Open();
            }
        }

        public void Open()
        {
            root.SetActive(true);
            ShowMain();
            MenuState.Open(this);
            Time.timeScale = 0f;
        }

        public void Close()
        {
            root.SetActive(false);
            MenuState.Close(this);
            Time.timeScale = 1f;
            EventSystem.current?.SetSelectedGameObject(null);
        }

        void ShowMain()
        {
            mainPage.SetActive(true);
            settingsPage.SetActive(false);
        }

        void ShowSettings()
        {
            mainPage.SetActive(false);
            settingsPage.SetActive(true);
            ShowTab(true);
            RefreshAll();
        }

        void ShowTab(bool graphics)
        {
            graphicsTab.SetActive(graphics);
            controlsTab.SetActive(!graphics);
            Tint(graphicsTabButton, graphics);
            Tint(controlsTabButton, !graphics);
        }

        void Tint(Button button, bool active)
        {
            var colors = button.colors;
            colors.normalColor = colors.selectedColor = active ? theme.accent : theme.button;
            button.colors = colors;
            button.GetComponentInChildren<TextMeshProUGUI>().color = active ? theme.accentText : theme.text;
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------------------------------------------------------------- building

        void Build()
        {
            var dim = UiFactory.Image(transform, "Dim", new Color(0.02f, 0.02f, 0.03f, 0.6f), null, true);
            dim.rectTransform.Fill();
            root = dim.gameObject;

            // Main page: title + three buttons.
            var main = UiFactory.Panel(dim.transform, "Main", theme, theme.panel, true).rectTransform;
            main.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 470f));
            mainPage = main.gameObject;
            Header(main, "PAUSED");
            var buttons = UiFactory.Rect("Buttons", main).Fill(48f, 40f, 48f, 120f);
            UiFactory.VerticalList(buttons, 16f);
            AddListButton(buttons, "RESUME", Close, true);
            AddListButton(buttons, "SETTINGS", ShowSettings, false);
            AddListButton(buttons, "QUIT TO DESKTOP", Quit, false);

            // Settings page: tabs + rows.
            var settings = UiFactory.Panel(dim.transform, "Settings", theme, theme.panel, true).rectTransform;
            settings.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 860f));
            settingsPage = settings.gameObject;
            Header(settings, "SETTINGS");

            graphicsTabButton = UiFactory.Button(settings, "GraphicsTab", theme, "GRAPHICS", () => ShowTab(true), false, 24f);
            ((RectTransform)graphicsTabButton.transform).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -104f), new Vector2(200f, 50f));
            controlsTabButton = UiFactory.Button(settings, "ControlsTab", theme, "CONTROLS", () => ShowTab(false), false, 24f);
            ((RectTransform)controlsTabButton.transform).Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(252f, -104f), new Vector2(200f, 50f));

            var body = UiFactory.Image(settings, "Body", theme.panelInset, theme.rounded).rectTransform;
            body.Fill(40f, 104f, 40f, 170f);
            graphicsTab = UiFactory.Rect("Graphics", body).Fill(28f, 16f, 28f, 16f).gameObject;
            UiFactory.VerticalList((RectTransform)graphicsTab.transform, 2f);
            controlsTab = UiFactory.Rect("Controls", body).Fill(28f, 16f, 28f, 16f).gameObject;
            UiFactory.VerticalList((RectTransform)controlsTab.transform, 2f);
            BuildGraphicsRows((RectTransform)graphicsTab.transform);
            BuildControlRows((RectTransform)controlsTab.transform);

            var back = UiFactory.Button(settings, "Back", theme, "BACK", ShowMain, true, 24f);
            ((RectTransform)back.transform).Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 30f), new Vector2(200f, 56f));
            var note = UiFactory.Text(settings, "Note", theme, "Changes apply right away and are saved.", 20f, theme.textDim);
            note.rectTransform.Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(44f, 42f), new Vector2(560f, 30f));
        }

        void Header(RectTransform panel, string title)
        {
            var stripes = UiFactory.Hazard(panel, "Stripes", theme);
            stripes.rectTransform.anchorMin = new Vector2(0f, 1f);
            stripes.rectTransform.anchorMax = new Vector2(1f, 1f);
            stripes.rectTransform.offsetMin = new Vector2(14f, -24f);
            stripes.rectTransform.offsetMax = new Vector2(-14f, -12f);
            var text = UiFactory.Text(panel, "Title", theme, title, 50f, theme.text, true);
            text.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -32f), new Vector2(600f, 60f));
        }

        void AddListButton(RectTransform parent, string label, UnityEngine.Events.UnityAction action, bool accent)
        {
            var button = UiFactory.Button(parent, label, theme, label, action, accent, 30f);
            button.gameObject.AddComponent<LayoutElement>().preferredHeight = 74f;
        }

        // ---------------------------------------------------------------- graphics

        static readonly string[] PresetNames = { "Very Low", "Low", "Medium", "High", "Ultra", "Extreme" };
        static readonly string[] AaNames = { "Off", "FXAA", "SMAA", "MSAA 2x", "MSAA 4x", "MSAA 8x", "TAA" };
        static readonly string[] ShadowNames = { "Off", "Low", "Medium", "High", "Ultra" };
        static readonly string[] AoNames = { "Off", "Medium", "High" };
        static readonly string[] DetailNames = { "Low", "Medium", "High", "Ultra" };
        static readonly string[] TextureNames = { "Full", "Half", "Quarter" };
        static readonly string[] WindowNames = { "Fullscreen", "Borderless", "Windowed" };
        static readonly FullScreenMode[] WindowModes = { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };
        static readonly int[] FpsLimits = { 0, 30, 60, 120, 144, 240 };
        static readonly string[] FpsNames = { "Unlimited", "30", "60", "120", "144", "240" };

        void BuildGraphicsRows(RectTransform list)
        {
            var preset = OptionSelector.Create(UiFactory.Row(list, "Quality preset", theme), theme, PresetNames, 2,
                i => GraphicsSettingsManager.SetPreset((GraphicsPreset)i));
            refreshers.Add(() =>
            {
                var s = GraphicsSettingsManager.Current;
                if (s.preset == GraphicsPreset.Custom)
                    preset.ShowText("Custom");
                else
                    preset.SetIndex((int)s.preset, false);
            });

            var scaleRow = UiFactory.Row(list, "Render scale", theme);
            var scaleLabel = UiFactory.Text(scaleRow, "Value", theme, "", 24f, theme.text, true, TextAlignmentOptions.MidlineRight);
            scaleLabel.rectTransform.Fill();
            var scale = UiFactory.Slider(scaleRow, "Slider", theme, 50f, 150f, 100f, v => Change(s => s.renderScale = Mathf.Round(v / 5f) * 5f / 100f));
            ((RectTransform)scale.transform).Fill(0f, 0f, 90f, 0f);
            refreshers.Add(() =>
            {
                var s = GraphicsSettingsManager.Current;
                scale.SetValueWithoutNotify(s.renderScale * 100f);
                scaleLabel.text = $"{s.renderScale * 100f:0}%";
            });

            Selector(list, "Anti-aliasing", AaNames, s => (int)s.antiAliasing, (s, i) => s.antiAliasing = (AntiAliasing)i);
            Selector(list, "Shadows", ShadowNames, s => (int)s.shadows, (s, i) => s.shadows = (ShadowQuality)i);
            Selector(list, "Ambient occlusion", AoNames, s => (int)s.ambientOcclusion, (s, i) => s.ambientOcclusion = (AmbientOcclusion)i);
            Selector(list, "Model detail", DetailNames, s => (int)s.modelDetail, (s, i) => s.modelDetail = (ModelDetail)i);
            Selector(list, "Textures", TextureNames, s => (int)s.textures, (s, i) => s.textures = (TextureQuality)i);
            Toggle(list, "Post-processing", s => s.postProcessing, (s, v) => s.postProcessing = v);
            Selector(list, "Window", WindowNames, s => Mathf.Max(0, Array.IndexOf(WindowModes, s.windowMode)), (s, i) => s.windowMode = WindowModes[i]);
            Toggle(list, "V-Sync", s => s.vSync, (s, v) => s.vSync = v);
            Selector(list, "FPS limit (V-Sync off)", FpsNames, s => Mathf.Max(0, Array.IndexOf(FpsLimits, s.frameRateLimit)), (s, i) => s.frameRateLimit = FpsLimits[i]);
        }

        void Selector(RectTransform list, string label, string[] names, Func<GraphicsSettingsData, int> get, Action<GraphicsSettingsData, int> set)
        {
            var selector = OptionSelector.Create(UiFactory.Row(list, label, theme), theme, names, 0, i => Change(s => set(s, i)));
            refreshers.Add(() => selector.SetIndex(get(GraphicsSettingsManager.Current), false));
        }

        void Toggle(RectTransform list, string label, Func<GraphicsSettingsData, bool> get, Action<GraphicsSettingsData, bool> set)
        {
            var toggle = ToggleSwitch.Create(UiFactory.Row(list, label, theme), theme, false, v => Change(s => set(s, v)));
            refreshers.Add(() => toggle.Set(get(GraphicsSettingsManager.Current), false));
        }

        static void Change(Action<GraphicsSettingsData> edit)
        {
            if (GraphicsSettingsManager.Current == null)
                return;
            var settings = GraphicsSettingsManager.Current.Clone();
            edit(settings);
            GraphicsSettingsManager.Apply(settings);
        }

        void OnGraphicsChanged(GraphicsSettingsData _)
        {
            if (IsOpen && !refreshing)
                RefreshAll();
        }

        void RefreshAll()
        {
            if (GraphicsSettingsManager.Current == null)
                return;
            refreshing = true;
            foreach (var refresh in refreshers)
                refresh();
            refreshing = false;
        }

        // ---------------------------------------------------------------- controls

        void BuildControlRows(RectTransform list)
        {
            var sensRow = UiFactory.Row(list, "Mouse sensitivity", theme);
            var sensLabel = UiFactory.Text(sensRow, "Value", theme, "", 24f, theme.text, true, TextAlignmentOptions.MidlineRight);
            sensLabel.rectTransform.Fill();
            var sens = UiFactory.Slider(sensRow, "Slider", theme, 0.2f, 3f, 1f, v => ChangeControls(c => c.mouseSensitivity = Mathf.Round(v * 20f) / 20f));
            ((RectTransform)sens.transform).Fill(0f, 0f, 90f, 0f);
            refreshers.Add(() =>
            {
                sens.SetValueWithoutNotify(ControlSettings.Current.mouseSensitivity);
                sensLabel.text = $"{ControlSettings.Current.mouseSensitivity:0.00}x";
            });

            var invert = ToggleSwitch.Create(UiFactory.Row(list, "Invert look up/down", theme), theme, false, v => ChangeControls(c => c.invertY = v));
            refreshers.Add(() => invert.Set(ControlSettings.Current.invertY, false));

            var fovRow = UiFactory.Row(list, "Field of view", theme);
            var fovLabel = UiFactory.Text(fovRow, "Value", theme, "", 24f, theme.text, true, TextAlignmentOptions.MidlineRight);
            fovLabel.rectTransform.Fill();
            var fov = UiFactory.Slider(fovRow, "Slider", theme, 55f, 100f, 70f, v => ChangeControls(c => c.fieldOfView = Mathf.Round(v)));
            ((RectTransform)fov.transform).Fill(0f, 0f, 90f, 0f);
            refreshers.Add(() =>
            {
                fov.SetValueWithoutNotify(ControlSettings.Current.fieldOfView);
                fovLabel.text = $"{ControlSettings.Current.fieldOfView:0}°";
            });

            var help = UiFactory.Text(list, "Help", theme,
                "Move: WASD   ·   Sprint: Shift   ·   Crouch: C   ·   Jump: Space\nLook: mouse   ·   Use / grab: hold left click   ·   Move held part: scroll\nMenu: Esc",
                20f, theme.textDim);
            help.textWrappingMode = TextWrappingModes.Normal;
            help.gameObject.AddComponent<LayoutElement>().preferredHeight = 110f;
        }

        void ChangeControls(Action<ControlSettingsData> edit)
        {
            var settings = ControlSettings.Current.Clone();
            edit(settings);
            ControlSettings.Apply(settings);
            RefreshAll();
        }
    }
}
