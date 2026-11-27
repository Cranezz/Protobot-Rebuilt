using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Protobot.Interface {
    // V2: Preferences, laid out like the Graphics and Keyboard shortcuts windows: tabs down the left (General, Camera,
    // Parts list), each setting a row with ‹ value › arrows and a dot per choice (click the value or turn the wheel
    // over it to step), and a panel on the right telling what the setting under the mouse does. Changes apply at once.
    // The keys moved to Edit > Keyboard shortcuts and the graphics to Edit > Graphics (buttons in the footer).
    // The original's settings are kept where they were: its Setting objects (camera speeds, invert, light mode: saved
    // in PlayerPrefs by name) and GameElementPreferences; the original Preferences pop-up opens this window instead.
    public class PreferencesWindow : MonoBehaviour {
        private enum Tab { General, Camera, PartsList }

        private class Option {
            public string title, description;
            public string[] choices;
            public Func<int> get;
            public Action<int> set;
        }

        private class Row {
            public Option option;
            public TextMeshProUGUI value;
            public Button left, right;
            public Image[] pips;
        }

        private static PreferencesWindow open;
        private Dialog dialog;
        private Tab tab = Tab.General;
        private RectTransform content;
        private readonly Dictionary<Tab, Image> tabButtons = new();
        private readonly List<Row> rows = new();
        private TextMeshProUGUI infoTitle, infoValue, infoBody;

        private static readonly float[] MoveSteps = { 1f / 64f, 1f / 32f, 1f / 16f, 1f / 8f, 1f / 4f, 1f / 2f, 1f, 2f };
        private static readonly float[] TurnSteps = { 1f, 5f, 7.5f, 15f, 22.5f, 30f, 45f, 90f };
        private static readonly string[] Speeds = { "1 (slowest)", "2", "3", "4", "5", "6", "7", "8", "9 (fastest)" };

        // ------------------------------------------------------------------ opening

        // V2 (build 77): a section of the Settings window now.
        public static void Open() => SettingsWindow.Open(SettingsWindow.Section.Preferences);

        internal static PreferencesWindow Fill(Dialog dialog) {
            open = dialog.gameObject.AddComponent<PreferencesWindow>();
            open.dialog = dialog;
            open.Build();
            open.ShowTab(Tab.General);
            return open;
        }

        // The original Preferences pop-up (opened from anywhere) opens this window instead.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Redirect() {
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                if (t.name == "Preferences" && t.gameObject.scene.IsValid() && t.Find("Category Selection") != null) {
                    t.gameObject.GetOrAdd<OldPreferences>();
                    if (t.gameObject.activeInHierarchy) t.gameObject.SetActive(false);
                }
        }

        private class OldPreferences : MonoBehaviour {
            private void OnEnable() {
                Open();
                var close = GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Close Button");
                if (close != null) close.onClick.Invoke();
                gameObject.SetActive(false);
            }
        }

        private void OnDestroy() {
            if (open == this) open = null;
        }

        // ------------------------------------------------------------------ layout

        private void Build() {
            var body = dialog.Body;
            var layout = Ui.Row(body, 18f);
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandHeight = true;

            var nav = Ui.Rect("Tabs", body);
            Ui.Column(nav, 4f, new RectOffset(0, 0, 4, 0));
            nav.Size(176f, -1f, 0f);
            TabButton(nav, Tab.General, "General");
            TabButton(nav, Tab.Camera, "Camera");
            TabButton(nav, Tab.PartsList, "Parts list");

            Ui.Divider(body, true);

            var frame = Ui.Rect("Settings", body);
            frame.Size(-1, -1, 1f);
            frame.gameObject.AddComponent<RectMask2D>();
            content = Ui.Rect("Rows", frame);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            Ui.Column(content, 2f, new RectOffset(0, 8, 0, 8));
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = frame.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = frame;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            var infoPanel = Ui.Image(body, "Info", Theme.PanelRaised, 10f);
            infoPanel.Size(280f, -1f, 0f);
            var info = infoPanel.rectTransform;
            Ui.Column(info, 10f, new RectOffset(18, 18, 18, 18));
            infoTitle = Ui.Text(info, "Title", "", Theme.FontTitle + 2f, Theme.Text, Theme.Medium);
            infoTitle.textWrappingMode = TextWrappingModes.Normal;
            infoValue = Ui.Text(info, "Value", "", Theme.FontSmall, Theme.Accent, Theme.Medium);
            Ui.Divider(info);
            infoBody = Ui.Text(info, "Body", "", Theme.FontBody - 1f, Theme.TextDim);
            infoBody.textWrappingMode = TextWrappingModes.Normal;
            infoBody.alignment = TextAlignmentOptions.TopLeft;
            infoBody.lineSpacing = 6f;
            infoBody.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

            var footerLayout = dialog.Footer.GetComponent<HorizontalLayoutGroup>();
            footerLayout.childAlignment = TextAnchor.MiddleRight;
            // V2 (build 77): Keyboard shortcuts and Graphics are sections beside this one in Settings' title bar.
            var done = Ui.Button(dialog.Footer, "Done", "Done", dialog.Close, true, 6f);
            done.Size(-1f, 36f);
        }

        private void TabButton(RectTransform parent, Tab t, string label) {
            var button = Ui.Button(parent, label, null, () => ShowTab(t), false, 7f);
            button.Size(-1, 42f);
            var background = (Image)button.targetGraphic;
            background.color = new Color(1f, 1f, 1f, 0f);
            var accent = Ui.Image(button.transform, "Accent", Theme.Accent, 2f);
            accent.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(3f, 22f));
            var text = Ui.Text(button.transform, "Label", label, Theme.FontBody + 1f, Theme.TextDim, Theme.Medium);
            text.rectTransform.Fill(16, 0, 8, 0);
            tabButtons[t] = background;
        }

        private void ShowTab(Tab t) {
            tab = t;
            foreach (var pair in tabButtons) {
                var selected = pair.Key == t;
                pair.Value.color = selected ? Theme.Hover : new Color(1f, 1f, 1f, 0f);
                pair.Value.transform.Find("Accent").gameObject.SetActive(selected);
                pair.Value.transform.Find("Label").GetComponent<TextMeshProUGUI>().color = selected ? Theme.Text : Theme.TextDim;
            }
            foreach (Transform child in content) Destroy(child.gameObject);
            rows.Clear();
            switch (t) {
                case Tab.General: BuildGeneral(); break;
                case Tab.Camera: BuildCamera(); break;
                case Tab.PartsList: BuildPartsList(); break;
            }
            Refresh();
            ShowInfo(null);
        }

        // ------------------------------------------------------------------ tabs

        private void BuildGeneral() {
            Section("Look");
            AddRow(new Option {
                title = "Light mode",
                description = "A light grid and background in the 3D view instead of the dark one.",
                choices = new[] { "Off", "On" },
                get = () => GetBool("Light Mode") ? 1 : 0, set = i => SetSetting("Light Mode", i == 1),
            });
            Section("Saving");
            AddRow(new Option {
                title = "Autosave",
                description = "Saves the open build every minute once it has a file (File > Save as…). A copy of each save is kept in the backups folder (Edit > Open backups folder): the last 10, one every 10 minutes.",
                choices = new[] { "Off", "Every minute" },
                get = () => Builds.AutoSave.Enabled ? 1 : 0, set = i => Builds.AutoSave.Enabled = i == 1,
            });
            // V2 (build 85): Discord Rich Presence (Online/DiscordPresence).
            Section("Discord");
            AddRow(new Option {
                title = "Show on Discord",
                description = "While Discord is running on this computer, your Discord profile shows that you're using Protobot and what you're doing (building, simulating, playing a game, writing code), like games do. Protobot only tells the Discord app on this computer; nothing else is sent.",
                choices = new[] { "Off", "On" },
                get = () => Online.DiscordPresence.Enabled ? 1 : 0, set = i => Online.DiscordPresence.Enabled = i == 1,
            });
            AddRow(new Option {
                title = "Build name on Discord",
                description = "Also show the open build's name (its file name) on your Discord profile. Off: only what you're doing shows.",
                choices = new[] { "Hidden", "Shown" },
                get = () => Online.DiscordPresence.ShowBuildName ? 1 : 0, set = i => Online.DiscordPresence.ShowBuildName = i == 1,
            });
            Section("Snapping");
            AddRow(new Option {
                title = "Move step",
                description = "How far a drag of the Transform tool's arrows (and the mirror's gap) jumps at a time. Hold Shift while dragging to move freely. Also in the Transform tool's options.",
                choices = MoveSteps.Select(s => Inch.Format(s)).ToArray(),
                get = () => Closest(MoveSteps, Tools.TransformGizmo.MoveStep), set = i => Tools.TransformGizmo.MoveStep = MoveSteps[i],
            });
            AddRow(new Option {
                title = "Turn step",
                description = "How far a drag of the Transform tool's rotate rings turns at a time; the dial shows a tick for each step. Also in the Transform tool's options.",
                choices = TurnSteps.Select(s => s.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "°").ToArray(),
                get = () => Closest(TurnSteps, Tools.TransformGizmo.TurnStep), set = i => Tools.TransformGizmo.TurnStep = TurnSteps[i],
            });
        }

        private void BuildCamera() {
            Section("Speed");
            SpeedRow("Orbit speed", "Orbit Sensitivity", "Orbit Sen Slider", 0.01f, 0.2f,
                "How fast the view turns round the build when you drag with the orbit button.");
            SpeedRow("Pan speed", "Pan Sensitivity", "Pan Sen Slider", 0.001f, 0.01f,
                "How fast the view slides when you drag with the pan button.");
            SpeedRow("Zoom speed", "Zoom Sensitivitiy", "Zoom Sen Slider", 0.001f, 0.01f,
                "How far each turn of the mouse wheel zooms.");
            Section("Direction");
            AddRow(new Option {
                title = "Invert orbit",
                description = "Dragging up turns the view the other way.",
                choices = new[] { "Off", "On" },
                get = () => GetBool("Invert Orbit") ? 1 : 0, set = i => SetSetting("Invert Orbit", i == 1),
            });
            AddRow(new Option {
                title = "Invert zoom",
                description = "The mouse wheel zooms the other way.",
                choices = new[] { "Off", "On" },
                get = () => GetBool("Invert Zoom") ? 1 : 0, set = i => SetSetting("Invert Zoom", i == 1),
            });
        }

        private void BuildPartsList() {
            Section("Older games' field elements");
            GameRow("Push Back", GameElementPreferences.PushBackKey, "Push Back's blocks, goals and loaders.");
            GameRow("High Stakes", GameElementPreferences.HighStakesKey, "High Stakes' field, rings and stakes.");
            GameRow("Spin Up", GameElementPreferences.SpinUpKey, "Spin Up's discs.");
        }

        private void GameRow(string game, string key, string what) {
            AddRow(new Option {
                title = "Show " + game,
                description = what + " in the parts list's Field section. Builds that use them always open with them, shown or not.",
                choices = new[] { "Off", "On" },
                get = () => PlayerPrefs.GetInt(key, 0) != 0 ? 1 : 0, set = i => GameElementPreferences.Set(key, i == 1),
            });
        }

        private void SpeedRow(string title, string setting, string slider, float min, float max, string description) {
            var original = FindSlider(slider);
            if (original != null) { min = original.minValue; max = original.maxValue; }
            AddRow(new Option {
                title = title, description = description, choices = Speeds,
                get = () => Mathf.Clamp(Mathf.RoundToInt(Mathf.InverseLerp(min, max, GetFloat(setting, max)) * (Speeds.Length - 1)), 0, Speeds.Length - 1),
                set = i => {
                    var value = Mathf.Lerp(min, max, i / (float)(Speeds.Length - 1));
                    SetSetting(setting, value);
                    if (original != null) original.SetValueWithoutNotify(value);
                },
            });
        }

        // ------------------------------------------------------------------ the original's settings

        private static Setting FindSetting(string name) =>
            FindObjectsByType<Setting>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(s => s.name == name);

        private static Slider FindSlider(string name) =>
            FindObjectsByType<Slider>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(s => s.name == name);

        private static bool GetBool(string name) {
            var setting = FindSetting(name);
            return setting != null && setting.RealValue is bool b && b;
        }

        private static float GetFloat(string name, float fallback) {
            var setting = FindSetting(name);
            if (setting == null) return fallback;
            return setting.RealValue switch { float f => f, int i => i, _ => fallback };
        }

        private static void SetSetting(string name, bool value) {
            var setting = FindSetting(name);
            if (setting == null) return;
            setting.SetValue(value);
            // Keep the original's switch showing the same (it isn't shown any more, but it's the source on start).
            foreach (var toggle in FindObjectsByType<Toggle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                for (var i = 0; i < toggle.onValueChanged.GetPersistentEventCount(); i++)
                    if (toggle.onValueChanged.GetPersistentTarget(i) == setting) toggle.SetIsOnWithoutNotify(value);
        }

        private static void SetSetting(string name, float value) {
            var setting = FindSetting(name);
            if (setting == null) return;
            setting.SetValue(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // ------------------------------------------------------------------ rows

        private void Section(string title) {
            var heading = Dialog.Heading(content, title);
            heading.margin = new Vector4(2, 14, 0, 2);
            heading.Size(-1, 38f);
        }

        private void AddRow(Option option) {
            var background = Ui.Image(content, option.title, new Color(1f, 1f, 1f, 0f), 7f, true);
            var rect = background.rectTransform;
            rect.Size(-1, 52f);
            Ui.Row(rect, 12f, new RectOffset(14, 10, 0, 0));
            background.gameObject.AddComponent<HoverTint>().target = background;
            var row = new Row { option = option };
            var hover = background.gameObject.AddComponent<RowHover>();
            hover.enter = () => ShowInfo(option);
            hover.wheel = d => Step(row, d);

            var label = Ui.Text(rect, "Label", option.title, Theme.FontBody + 1f, Theme.Text);
            label.Size(-1, 52f, 1f);

            var selector = Ui.Image(rect, "Selector", Theme.Field, 7f, true);
            selector.Size(260f, 40f);
            row.left = Arrow(selector.transform, "‹", () => Step(row, -1), true);
            row.right = Arrow(selector.transform, "›", () => Step(row, +1), false);
            var middle = Ui.Rect("Value", selector.transform).Fill(40, 0, 40, 0);
            var cycle = middle.gameObject.AddComponent<Image>();
            cycle.color = new Color(1f, 1f, 1f, 0f);
            var click = middle.gameObject.AddComponent<Button>();
            click.transition = Selectable.Transition.None;
            click.onClick.AddListener(() => Step(row, +1, true));
            row.value = Ui.Text(middle, "Text", "", Theme.FontBody, Theme.Text, Theme.Medium, TextAlignmentOptions.Center);
            row.value.rectTransform.Fill(0, 0, 0, 10);
            var pips = Ui.Rect("Pips", middle);
            pips.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(170f, 4f));
            var pipsLayout = Ui.Row(pips, 4f, null, TextAnchor.MiddleCenter);
            pipsLayout.childControlWidth = pipsLayout.childControlHeight = false;
            var count = option.choices.Length;
            row.pips = new Image[count > 1 && count <= 9 ? count : 0];
            if (row.pips.Length == 0) row.value.rectTransform.Fill();
            var width = Mathf.Min(22f, (160f - 4f * (count - 1)) / Mathf.Max(1, count));
            for (var i = 0; i < row.pips.Length; i++) {
                row.pips[i] = Ui.Image(pips, "Pip", Theme.Hover, 1.5f);
                row.pips[i].rectTransform.sizeDelta = new Vector2(width, 3f);
            }
            rows.Add(row);
        }

        private static Button Arrow(Transform parent, string glyph, Action onClick, bool left) {
            var button = Ui.Button(parent, left ? "Left" : "Right", null, onClick, false, 6f);
            button.targetGraphic.color = new Color(1f, 1f, 1f, 0f);
            ((RectTransform)button.transform).Place(new Vector2(left ? 0f : 1f, 0.5f), new Vector2(left ? 0f : 1f, 0.5f),
                new Vector2(left ? 2f : -2f, 0f), new Vector2(36f, 36f));
            var text = Ui.Text(button.transform, "Glyph", glyph, 26f, Theme.Text, Theme.Medium, TextAlignmentOptions.Center);
            text.rectTransform.Fill(0, 0, 0, 3);
            return button;
        }

        private void Step(Row row, int direction, bool wrap = false) {
            var count = row.option.choices.Length;
            var index = row.option.get() + direction;
            if (wrap) index = (index + count) % count;
            if (index < 0 || index >= count) return;
            row.option.set(index);
            Interface.Say($"{row.option.title}: {row.option.choices[index]}");
            Refresh();
            ShowInfo(row.option);
        }

        private void Refresh() {
            foreach (var row in rows) {
                var index = Mathf.Clamp(row.option.get(), 0, row.option.choices.Length - 1);
                row.value.text = row.option.choices[index];
                row.left.interactable = index > 0;
                row.right.interactable = index < row.option.choices.Length - 1;
                for (var i = 0; i < row.pips.Length; i++)
                    row.pips[i].color = i == index ? Theme.Accent : Theme.Hover;
            }
        }

        private void ShowInfo(Option option) {
            if (option == null) {
                infoTitle.text = tab switch { Tab.Camera => "Camera", Tab.PartsList => "Parts list", _ => "General" };
                infoValue.text = "";
                infoBody.text = tab switch {
                    Tab.Camera => "How the view moves when you orbit, pan and zoom.",
                    Tab.PartsList => "What the parts list shows. Older games' field elements are hidden unless you switch them on here.",
                    _ => "Point at a setting to see what it does. Click its value or turn the mouse wheel over it to change it.\n\nKeys are in Keyboard shortcuts, the look and speed of the 3D view in Graphics (tabs at the top). The camera speeds are under Camera.",
                };
                return;
            }
            infoTitle.text = option.title;
            infoValue.text = "Now: " + option.choices[Mathf.Clamp(option.get(), 0, option.choices.Length - 1)];
            infoBody.text = option.description;
        }

        private static int Closest(float[] values, float value) {
            var best = 0;
            for (var i = 1; i < values.Length; i++)
                if (Mathf.Abs(values[i] - value) < Mathf.Abs(values[best] - value)) best = i;
            return best;
        }

        private class RowHover : MonoBehaviour, IPointerEnterHandler, IScrollHandler {
            public Action enter;
            public Action<int> wheel;
            public void OnPointerEnter(PointerEventData eventData) => enter?.Invoke();
            public void OnScroll(PointerEventData eventData) {
                if (wheel == null || Mathf.Abs(eventData.scrollDelta.y) < 0.01f) {
                    ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, eventData, ExecuteEvents.scrollHandler);
                    return;
                }
                wheel(eventData.scrollDelta.y > 0 ? 1 : -1);
            }
        }
    }
}
