using System;
using System.Collections.Generic;
using System.Linq;
using Protobot.GraphicsQuality;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Protobot.Interface {
    // V2: the graphics settings, laid out like a game's: tabs down the left (Display, Quality, Advanced, Benchmark),
    // each setting a row with ‹ value › arrows and a dot per choice (click the value or turn the wheel over it to
    // step), the presets as tiles, and a panel on the right telling what the setting under the mouse does and what
    // it costs. Changes show at once; Revert puts back what was set when the window opened. The footer shows the
    // frame rate live. Settings are applied and saved by GraphicsSettingsManager (Protobot.Runtime).
    // Open: Edit > Graphics…, or Preferences > Graphics.
    public class GraphicsWindow : MonoBehaviour {
        private enum Tab { Display, Quality, Advanced, Benchmark }

        private class Option {
            public string title, description, affects;
            public int impact;                                   // 0 none .. 4 very high
            public string[] choices;
            public Func<GraphicsSettingsData, int> get;
            public Action<GraphicsSettingsData, int> set;
            public Func<GraphicsSettingsData, string> disabledWhy;  // a reason when it does nothing now, else null
            public bool display;                                 // window mode / resolution: asks to keep it (builds)
        }

        private class Row {
            public Option option;
            public TextMeshProUGUI value, label, note;
            public Button left, right;
            public Image[] pips;
            public CanvasGroup group;
        }

        private static GraphicsWindow open;
        private Dialog dialog;
        private Tab tab = Tab.Quality;
        private GraphicsSettingsData opened;
        private RectTransform content, info;
        private readonly Dictionary<Tab, Image> tabButtons = new();
        private readonly List<Row> rows = new();
        private readonly List<(GraphicsPreset preset, Image background, TextMeshProUGUI name, Image[] meter, TextMeshProUGUI badge)> tiles = new();
        private Slider scaleSlider;
        private TextMeshProUGUI scaleValue, presetState, fpsText, frameText;
        private TextMeshProUGUI infoTitle, infoBody, infoAffects, infoValue;
        private Image[] impactBars;
        private GameObject impactRow;
        private Option hovered;
        private float smoothed = 1f / 60f, nextFpsUpdate;
        private bool rebuilding;

        // ------------------------------------------------------------------ opening

        public static void Open() => Open(null);

        private static void Open(Tab? start) {
            if (GraphicsSettingsManager.Current == null) {
                Interface.Say("The graphics settings aren't available (no URP asset)", false);
                return;
            }
            if (open != null) {
                if (start.HasValue) open.ShowTab(start.Value);
                return;
            }
            var dialog = Dialog.Open("Graphics", new Vector2(1120f, 720f));
            open = dialog.gameObject.AddComponent<GraphicsWindow>();
            open.dialog = dialog;
            open.opened = GraphicsSettingsManager.Current.Clone();
            open.Build();
            open.ShowTab(start ?? (GraphicsBenchmark.Last != null ? Tab.Benchmark : Tab.Quality));
        }

        private void OnDestroy() {
            GraphicsSettingsManager.Changed -= OnChanged;
            GraphicsBenchmark.Finished -= OnBenchmarkFinished;
            if (open == this) open = null;
        }

        // ------------------------------------------------------------------ layout

        private void Build() {
            var body = dialog.Body;
            var layout = Ui.Row(body, 18f);
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandHeight = true;

            // Tabs.
            var nav = Ui.Rect("Tabs", body);
            Ui.Column(nav, 4f, new RectOffset(0, 0, 4, 0));
            nav.Size(176f, -1f, 0f);
            foreach (Tab t in Enum.GetValues(typeof(Tab)))
                TabButton(nav, t);

            Ui.Divider(body, true);

            // The settings (scrolls when a tab is taller than the window).
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

            // What the setting under the mouse does.
            var infoPanel = Ui.Image(body, "Info", Theme.PanelRaised, 10f);
            infoPanel.Size(300f, -1f, 0f);
            info = infoPanel.rectTransform;
            Ui.Column(info, 10f, new RectOffset(18, 18, 18, 18));
            infoTitle = Ui.Text(info, "Title", "", Theme.FontTitle + 2f, Theme.Text, Theme.Medium);
            infoTitle.textWrappingMode = TextWrappingModes.Normal;
            infoValue = Ui.Text(info, "Value", "", Theme.FontSmall, Theme.Accent, Theme.Medium);
            impactRow = Ui.Rect("Impact", info).gameObject;
            Ui.Row((RectTransform)impactRow.transform, 4f);
            ((RectTransform)impactRow.transform).Size(-1, 22f);
            var impactLabel = Ui.Text(impactRow.transform, "Label", "Performance cost", Theme.FontSmall, Theme.TextDim);
            impactLabel.Size(128f, 22f);
            impactBars = new Image[4];
            for (var i = 0; i < 4; i++) {
                impactBars[i] = Ui.Image(impactRow.transform, "Bar " + i, Theme.Hover, 2f);
                impactBars[i].Size(22f, 8f);
            }
            infoAffects = Ui.Text(info, "Affects", "", Theme.FontSmall, Theme.TextDim);
            infoAffects.textWrappingMode = TextWrappingModes.Normal;
            Ui.Divider(info);
            infoBody = Ui.Text(info, "Body", "", Theme.FontBody - 1f, Theme.TextDim);
            infoBody.textWrappingMode = TextWrappingModes.Normal;
            infoBody.alignment = TextAlignmentOptions.TopLeft;
            infoBody.lineSpacing = 6f;
            infoBody.GetComponent<RectTransform>();
            var bodyLayout = infoBody.gameObject.AddComponent<LayoutElement>();
            bodyLayout.flexibleHeight = 1f;

            // Footer: the frame rate on the left, the buttons on the right.
            var footerLayout = dialog.Footer.GetComponent<HorizontalLayoutGroup>();
            footerLayout.childAlignment = TextAnchor.MiddleLeft;
            var meter = Ui.Rect("Frame Rate", dialog.Footer);
            Ui.Row(meter, 8f);
            meter.Size(-1, 36f, 1f);
            var dot = Ui.Image(meter, "Dot", Theme.Success, 0f);
            dot.sprite = Theme.Circle;
            dot.Size(10f, 10f);
            fpsText = Ui.Text(meter, "FPS", "", Theme.FontBody, Theme.Text, Theme.Medium);
            fpsText.Size(86f, 36f);
            frameText = Ui.Text(meter, "Detail", "", Theme.FontSmall, Theme.TextDim);
            frameText.Size(-1, 36f, 1f);
            Ui.Tip(meter, "Frame rate now", null, "Frames drawn per second and how long each one takes. With V-Sync on it stops at the monitor's refresh rate.");

            var reset = Ui.Button(dialog.Footer, "Recommended", "Use recommended", UseRecommended, false, 6f);
            reset.Size(-1f, 36f);
            Ui.Tip(reset, "Use recommended", null, "The preset picked for this PC (from the benchmark if you ran it, else from the graphics card's name).");
            var revert = Ui.Button(dialog.Footer, "Revert", "Revert", Revert, false, 6f);
            revert.Size(-1f, 36f);
            Ui.Tip(revert, "Revert", null, "Put back the settings as they were when this window opened.");
            var done = Ui.Button(dialog.Footer, "Done", "Done", dialog.Close, true, 6f);
            done.Size(-1f, 36f);

            GraphicsSettingsManager.Changed += OnChanged;
            GraphicsBenchmark.Finished += OnBenchmarkFinished;
        }

        private void TabButton(RectTransform parent, Tab t) {
            var label = t switch {
                Tab.Display => "Display",
                Tab.Quality => "Quality",
                Tab.Advanced => "Advanced",
                _ => "Benchmark",
            };
            var button = Ui.Button(parent, label, null, () => ShowTab(t), false, 7f);
            button.Size(-1, 42f);
            var background = (Image)button.targetGraphic;
            background.color = new Color(1f, 1f, 1f, 0f);
            var accent = Ui.Image(button.transform, "Accent", Theme.Accent, 2f);
            accent.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(3f, 22f));
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
            Rebuild();
        }

        private void Rebuild() {
            rebuilding = true;
            foreach (Transform child in content)
                Destroy(child.gameObject);
            rows.Clear();
            tiles.Clear();
            scaleSlider = null;
            switch (tab) {
                case Tab.Display: BuildDisplay(); break;
                case Tab.Quality: BuildQuality(); break;
                case Tab.Advanced: BuildAdvanced(); break;
                case Tab.Benchmark: BuildBenchmark(); break;
            }
            rebuilding = false;
            Refresh();
            ShowInfo(null);
        }

        // ------------------------------------------------------------------ tabs

        private void BuildDisplay() {
            Section("Screen");
            AddRow(WindowModeOption());
            AddRow(ResolutionOption());
            ScaleRow();
            Section("Frame rate");
            AddRow(new Option {
                title = "V-Sync", impact = 0, affects = "Smoothness",
                description = "Waits for the monitor before showing each frame: no tearing, and the frame rate stops at the monitor's refresh rate (60, 144…), so the graphics card works no harder than it needs to.\n\nTurn it off to see the real frame rate or to use a frame rate limit.",
                choices = new[] { "Off", "On" }, get = s => s.vSync ? 1 : 0, set = (s, i) => s.vSync = i == 1,
            });
            AddRow(new Option {
                title = "Frame rate limit", impact = 0, affects = "Power and heat",
                description = "The most frames drawn each second while V-Sync is off. A limit near your monitor's refresh rate saves power and keeps a laptop cooler (and a cool laptop doesn't slow down).",
                choices = FrameLimits.Select(f => f == 0 ? "Unlimited" : f + " FPS").ToArray(),
                get = s => Closest(FrameLimits, s.frameRateLimit), set = (s, i) => s.frameRateLimit = FrameLimits[i],
                disabledWhy = s => s.vSync ? "V-Sync is on: the monitor sets the frame rate" : null,
            });
            AddRow(new Option {
                title = "Lower frame rate when idle", impact = 0, affects = "Power, heat and fan noise",
                description = $"After {IdleFrameRate.IdleAfter:0} seconds without the mouse or keyboard the app draws {IdleFrameRate.IdleFps} frames a second, and {IdleFrameRate.BackgroundFps} while another window is in front. Nothing moves in a build you aren't touching, so you won't see a difference, but your battery and fans will.\n\nThe simulation, running motors and the benchmark always get the full frame rate.",
                choices = new[] { "Off", "On" }, get = s => s.idleFrameRate ? 1 : 0, set = (s, i) => s.idleFrameRate = i == 1,
            });
            AddRow(new Option {
                title = "Frame rate counter", impact = 0, affects = "Nothing",
                description = "Shows the frame rate in the bottom-left corner of the 3D view.",
                choices = new[] { "Off", "On" }, get = s => s.showFrameRate ? 1 : 0, set = (s, i) => s.showFrameRate = i == 1,
            });
        }

        private void BuildQuality() {
            Section("Preset");
            PresetTiles();
            Section("Lighting");
            AddRow(new Option {
                title = "Shadows", impact = 3, affects = "Graphics card (geometry)",
                description = "Shadows show how parts sit on each other and on the floor.\n\nEach shadow cascade draws the whole build once more, so on a big robot (or the field) they cost more than anything else here. Low: one cascade near the camera. Ultra: three cascades out to 150\" and sharper edges.",
                choices = new[] { "Off", "Low", "Medium", "High", "Ultra" },
                get = s => (int)s.shadows, set = (s, i) => s.shadows = (Protobot.GraphicsQuality.ShadowQuality)i,
            });
            AddRow(new Option {
                title = "Ambient occlusion", impact = 2, affects = "Graphics card (pixels)",
                description = "Soft shading in corners and gaps where parts meet, so holes, channels and screw heads read better.\n\nIts cost grows with the resolution.",
                choices = new[] { "Off", "Medium", "High" },
                get = s => (int)s.ambientOcclusion, set = (s, i) => s.ambientOcclusion = (AmbientOcclusion)i,
            });
            Section("Edges and textures");
            AddRow(new Option {
                title = "Anti-aliasing", impact = 2, affects = "Graphics card",
                description = "Smooths jagged edges.\n\nFXAA: cheapest, a little soft. SMAA: sharper, still cheap. MSAA: the cleanest edges on thin parts (draws each edge 2, 4 or 8 times). TAA: smooth, but blurs while the camera moves.",
                choices = new[] { "Off", "FXAA", "SMAA", "MSAA 2x", "MSAA 4x", "MSAA 8x", "TAA" },
                get = s => (int)s.antiAliasing, set = (s, i) => s.antiAliasing = (AntiAliasing)i,
            });
            AddRow(new Option {
                title = "Texture quality", impact = 1, affects = "Graphics memory",
                description = "The resolution of textures (plastic grain, field tiles). Lower it on graphics cards with 1 GB of memory or less, or on integrated graphics that share the PC's memory.",
                choices = new[] { "Low", "Medium", "High" },
                get = s => 2 - (int)s.textures, set = (s, i) => s.textures = (TextureQuality)(2 - i),
            });
            AddRow(new Option {
                title = "Texture filtering", impact = 1, affects = "Graphics card",
                description = "Anisotropic filtering keeps textures sharp on surfaces seen at a slant, like the field's tiles.",
                choices = new[] { "Bilinear", "Anisotropic" },
                get = s => s.anisotropicFiltering ? 1 : 0, set = (s, i) => s.anisotropicFiltering = i == 1,
            });
            Section("Detail");
            AddRow(new Option {
                title = "Hide tiny far-away parts", impact = 0, affects = "Graphics card (geometry): a big saving on weak PCs",
                description = "Every part is drawn from VEX's full-detail model, so a robot is millions of triangles, and on integrated graphics or an old card drawing them is nearly all the work. A screw across the field is a few pixels but still thousands of triangles.\n\nThis skips drawing any part smaller on screen than 2, 5 or 10 pixels (10: for integrated graphics; small bits of the field drop out in the overview). The models are never simplified: a part is drawn again, in full detail, as soon as you zoom in, and it can still be clicked. Not changed by the presets.",
                choices = new[] { "Off", "Under 2 px", "Under 5 px", "Under 10 px" },
                get = s => Mathf.Clamp(s.smallPartCulling, 0, 3), set = (s, i) => s.smallPartCulling = i,
            });
        }

        private void BuildAdvanced() {
            Section("Rendering");
            AddRow(new Option {
                title = "Post-processing", impact = 1, affects = "Graphics card (pixels)",
                description = "The full-screen pass after the 3D view is drawn. FXAA and SMAA anti-aliasing run in it, so it stays on while one of them is chosen.",
                choices = new[] { "Off", "On" }, get = s => s.postProcessing ? 1 : 0, set = (s, i) => s.postProcessing = i == 1,
                disabledWhy = s => s.antiAliasing is AntiAliasing.FXAA or AntiAliasing.SMAA ? "Needed by " + AaName(s.antiAliasing) : null,
            });
            AddRow(new Option {
                title = "Upscaling", impact = 0, affects = "Sharpness",
                description = "How the 3D view is stretched to the window when the render scale is under 100%. FSR (AMD FidelityFX Super Resolution) keeps edges sharp; it works on every graphics card.",
                choices = new[] { "FSR" }, get = _ => 0, set = (_, _) => { },
                disabledWhy = s => s.renderScale >= 0.99f ? "Only used below 100% render scale" : null,
            });
            Section("This PC");
            InfoLine("Graphics card", SystemInfo.graphicsDeviceName);
            InfoLine("Kind", GraphicsHardwareDetector.Describe(SystemInfo.graphicsDeviceName)
                             + (SystemInfo.graphicsMemorySize > 0 ? $", {SystemInfo.graphicsMemorySize / 1024f:0.#} GB" : ""));
            InfoLine("Graphics API", SystemInfo.graphicsDeviceVersion);
            InfoLine("Processor", $"{SystemInfo.processorType.Trim()} ({SystemInfo.processorCount} threads)");
            InfoLine("Memory", $"{SystemInfo.systemMemorySize / 1024f:0} GB");
            InfoLine("Picked for this PC", GraphicsBenchmark.PresetName(Recommended()) + (BenchmarkPick().HasValue ? " (from the benchmark)" : " (from the graphics card)"));
        }

        private void BuildBenchmark() {
            Section("Benchmark");
            var intro = Ui.Text(content, "Intro",
                "Draws the build around a circle at each preset, then shows how fast each one ran on this PC and picks the best one that stays smooth. " +
                "Load the build you work on first: a bigger robot is harder to draw. It takes about 40 seconds; Esc stops it.",
                Theme.FontBody - 1f, Theme.TextDim);
            intro.textWrappingMode = TextWrappingModes.Normal;
            intro.Size(-1, 64f);

            var buttons = Ui.Rect("Buttons", content);
            Ui.Row(buttons, 10f);
            buttons.Size(-1, 44f);
            var run = Ui.Button(buttons, "Run", GraphicsBenchmark.Last != null ? "Run again" : "Run benchmark", StartBenchmark, true, 6f);
            run.Size(-1f, 38f);
            if (GraphicsBenchmark.Last != null && BenchmarkPick() is GraphicsPreset pick) {
                var use = Ui.Button(buttons, "Use", "Use " + GraphicsBenchmark.PresetName(pick), () => {
                    GraphicsSettingsManager.SetPreset(pick);
                    Interface.Say("Graphics preset: " + GraphicsBenchmark.PresetName(pick));
                }, false, 6f);
                use.Size(-1f, 38f);
            }

            var results = GraphicsBenchmark.Last;
            if (results == null || results.Count == 0) {
                var none = Ui.Text(content, "None", "No results yet.", Theme.FontBody, Theme.TextFaint);
                none.Size(-1, 40f);
                return;
            }
            Section($"Results at {results[0].width} x {results[0].height}");
            var best = results.Max(r => r.averageFps);
            foreach (var r in results)
                ResultRow(r, best);

            var verdict = Ui.Text(content, "Verdict", Verdict(results), Theme.FontBody - 1f, Theme.Text);
            verdict.textWrappingMode = TextWrappingModes.Normal;
            verdict.margin = new Vector4(0, 10, 0, 0);
            verdict.Size(-1, 84f);
        }

        // ------------------------------------------------------------------ rows

        private void Section(string title) {
            var heading = Dialog.Heading(content, title);
            heading.margin = new Vector4(2, 14, 0, 2);
            heading.Size(-1, 38f);
        }

        private void InfoLine(string label, string value) {
            var row = Ui.Rect(label, content);
            Ui.Row(row, 12f, new RectOffset(14, 14, 0, 0));
            row.Size(-1, 34f);
            Ui.Text(row, "Label", label, Theme.FontBody, Theme.TextDim).Size(200f, 34f);
            var text = Ui.Text(row, "Value", value, Theme.FontBody, Theme.Text);
            text.Size(-1, 34f, 1f);
        }

        private Row AddRow(Option option) {
            var background = Ui.Image(content, option.title, new Color(1f, 1f, 1f, 0f), 7f, true);
            var rect = background.rectTransform;
            rect.Size(-1, 52f);
            Ui.Row(rect, 12f, new RectOffset(14, 10, 0, 0));
            background.gameObject.AddComponent<HoverTint>().target = background;
            var row = new Row { option = option, group = background.gameObject.AddComponent<CanvasGroup>() };
            var hover = background.gameObject.AddComponent<RowHover>();
            hover.enter = () => ShowInfo(option);
            hover.wheel = d => Step(row, d);

            var labels = Ui.Rect("Labels", rect);
            Ui.Column(labels, 0f).childAlignment = TextAnchor.MiddleLeft;
            labels.Size(-1, 52f, 1f);
            row.label = Ui.Text(labels, "Label", option.title, Theme.FontBody + 1f, Theme.Text);
            row.label.Size(-1, 24f);
            row.note = Ui.Text(labels, "Note", "", Theme.FontSmall - 1f, Theme.TextFaint);
            row.note.Size(-1, 18f);

            // ‹ value › with a dot per choice under the value.
            var selector = Ui.Image(rect, "Selector", Theme.Field, 7f, true);
            selector.Size(300f, 40f);
            row.left = Arrow(selector.transform, "‹", () => Step(row, -1), true);
            row.right = Arrow(selector.transform, "›", () => Step(row, +1), false);
            var middle = Ui.Rect("Value", selector.transform).Fill(40, 0, 40, 0);
            var cycle = middle.gameObject.AddComponent<Image>();
            cycle.color = new Color(1f, 1f, 1f, 0f);
            var click = middle.gameObject.AddComponent<Button>();
            click.transition = Selectable.Transition.None;
            click.onClick.AddListener(() => Step(row, +1, wrap: true));
            row.value = Ui.Text(middle, "Text", "", Theme.FontBody, Theme.Text, Theme.Medium, TextAlignmentOptions.Center);
            row.value.rectTransform.Fill(0, 0, 0, 10);
            var pips = Ui.Rect("Pips", middle);
            pips.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(200f, 4f));
            var pipsLayout = Ui.Row(pips, 4f, null, TextAnchor.MiddleCenter);
            pipsLayout.childControlWidth = pipsLayout.childControlHeight = false;
            var count = option.choices.Length;
            row.pips = new Image[count > 1 && count <= 9 ? count : 0];  // a long list (resolutions) has none
            if (row.pips.Length == 0) row.value.rectTransform.Fill();
            var width = Mathf.Min(22f, (190f - 4f * (count - 1)) / Mathf.Max(1, count));
            for (var i = 0; i < row.pips.Length; i++) {
                row.pips[i] = Ui.Image(pips, "Pip", Theme.Hover, 1.5f);
                row.pips[i].rectTransform.sizeDelta = new Vector2(width, 3f);
            }
            rows.Add(row);
            return row;
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
            var current = GraphicsSettingsManager.Current;
            if (current == null || row.option.disabledWhy?.Invoke(current) != null) {
                if (current != null) Interface.Say(row.option.title + ": " + row.option.disabledWhy(current), false);
                return;
            }
            var count = row.option.choices.Length;
            var index = row.option.get(current) + direction;
            if (wrap) index = (index + count) % count;
            if (index < 0 || index >= count) return;
            var settings = current.Clone();
            row.option.set(settings, index);
            ApplySettings(settings, row.option.display);
            Interface.Say($"{row.option.title}: {row.option.choices[index]}");
            ShowInfo(row.option);
        }

        // The resolution of the 3D view (the interface always draws at the window's resolution).
        private void ScaleRow() {
            var option = new Option {
                title = "Render scale", impact = 3, affects = "Graphics card (pixels)",
                description = "The resolution the 3D view is drawn at, compared to the window. Under 100% it's drawn smaller and scaled up with FSR: faster on integrated graphics, a little softer. Over 100% it's drawn bigger and scaled down: very sharp, very slow.\n\nThe interface always stays sharp.",
                choices = new[] { "" }, get = _ => 0, set = (_, _) => { },
            };
            var row = AddRow(option);
            var selector = row.left.transform.parent;
            foreach (Transform child in selector) Destroy(child.gameObject);
            ((Image)selector.GetComponent<Image>()).color = new Color(1f, 1f, 1f, 0f);
            rows.Remove(row);

            var area = Ui.Rect("Slider", selector).Fill(4, 0, 64, 0);
            var track = Ui.Image(area, "Track", Theme.Field, 3f);
            track.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            track.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            track.rectTransform.sizeDelta = new Vector2(0f, 6f);
            var fillArea = Ui.Rect("Fill Area", area);
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.sizeDelta = new Vector2(0f, 6f);
            var fill = Ui.Image(fillArea, "Fill", Theme.Accent, 3f);
            fill.rectTransform.sizeDelta = Vector2.zero;
            // The slider stretches the handle to its area's height: the area is as tall as the handle.
            var handleArea = Ui.Rect("Handle Area", area);
            handleArea.anchorMin = new Vector2(0f, 0.5f);
            handleArea.anchorMax = new Vector2(1f, 0.5f);
            handleArea.sizeDelta = new Vector2(-18f, 18f);
            var handle = Ui.Image(handleArea, "Handle", Color.white, 0f, true);
            handle.sprite = Theme.Circle;
            handle.rectTransform.sizeDelta = new Vector2(18f, 0f);
            var slider = area.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 10;   // 50% .. 200% in steps of 5
            slider.maxValue = 40;
            slider.wholeNumbers = true;
            var navigation = slider.navigation;
            navigation.mode = Navigation.Mode.None;
            slider.navigation = navigation;
            scaleValue = Ui.Text(selector, "Percent", "", Theme.FontBody, Theme.Text, Theme.Medium, TextAlignmentOptions.MidlineRight);
            scaleValue.rectTransform.Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-4f, 0f), new Vector2(60f, 36f));
            slider.onValueChanged.AddListener(v => {
                if (rebuilding) return;
                var settings = GraphicsSettingsManager.Current.Clone();
                settings.renderScale = v * 0.05f;
                ApplySettings(settings, false);
            });
            var up = area.gameObject.AddComponent<RowHover>();
            up.release = () => Interface.Say($"Render scale: {Mathf.RoundToInt(GraphicsSettingsManager.Current.renderScale * 100f)}%");
            var hover = row.group.GetComponent<RowHover>();
            hover.wheel = d => slider.value += d;
            scaleSlider = slider;
        }

        private void PresetTiles() {
            var strip = Ui.Rect("Presets", content);
            var layout = Ui.Row(strip, 8f);
            layout.childForceExpandWidth = true;
            strip.Size(-1, 86f);
            var recommended = Recommended();
            for (var p = GraphicsPreset.VeryLow; p <= GraphicsPreset.Extreme; p++) {
                var preset = p;
                var button = Ui.Button(strip, GraphicsBenchmark.PresetName(p), null, () => {
                    GraphicsSettingsManager.SetPreset(preset);
                    Interface.Say("Graphics preset: " + GraphicsBenchmark.PresetName(preset));
                }, false, 8f);
                button.Size(-1, 86f, 1f);
                var background = (Image)button.targetGraphic;
                var column = Ui.Rect("Column", button.transform).Fill(6, 10, 6, 8);
                var name = Ui.Text(column, "Name", GraphicsBenchmark.PresetName(p), Theme.FontBody, Theme.Text, Theme.Medium, TextAlignmentOptions.Top);
                name.rectTransform.Fill(0, 0, 0, 40);
                var meterRect = Ui.Rect("Meter", column);
                meterRect.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -4f), new Vector2(84f, 6f));
                var meterLayout = Ui.Row(meterRect, 3f, null, TextAnchor.MiddleCenter);
                meterLayout.childControlWidth = meterLayout.childControlHeight = false;
                var meter = new Image[6];
                for (var i = 0; i < 6; i++) {
                    meter[i] = Ui.Image(meterRect, "Step", Theme.Hover, 1.5f);
                    meter[i].rectTransform.sizeDelta = new Vector2(11f, 6f);
                }
                var badge = Ui.Text(column, "Badge", preset == recommended ? "FOR THIS PC" : "", 10.5f, Theme.Success, Theme.Medium, TextAlignmentOptions.Bottom);
                badge.characterSpacing = 4f;
                badge.overflowMode = TextOverflowModes.Overflow;
                badge.rectTransform.Fill(-6, 0, -6, 0);
                tiles.Add((p, background, name, meter, badge));
                var hover = button.gameObject.AddComponent<RowHover>();
                hover.enter = () => ShowPresetInfo(preset);
            }
            presetState = Ui.Text(content, "State", "", Theme.FontSmall, Theme.TextDim);
            presetState.margin = new Vector4(4, 2, 0, 0);
            presetState.Size(-1, 24f);
        }

        private void ResultRow(GraphicsBenchmark.Result r, float best) {
            var row = Ui.Rect(r.preset.ToString(), content);
            Ui.Row(row, 12f, new RectOffset(14, 10, 0, 0));
            row.Size(-1, 44f);
            Ui.Text(row, "Preset", GraphicsBenchmark.PresetName(r.preset), Theme.FontBody, Theme.Text, Theme.Medium).Size(92f, 44f);
            var barArea = Ui.Rect("Bar", row);
            barArea.Size(-1, 44f, 1f);
            var track = Ui.Image(barArea, "Track", Theme.Field, 4f);
            track.rectTransform.Fill(0, 12, 0, 12);
            var bar = Ui.Image(track.transform, "Fill", FpsColor(r), 4f);
            bar.rectTransform.anchorMin = Vector2.zero;
            bar.rectTransform.anchorMax = new Vector2(Mathf.Clamp(r.averageFps / Mathf.Max(best, 1f), 0.04f, 1f), 1f);
            bar.rectTransform.offsetMin = bar.rectTransform.offsetMax = Vector2.zero;
            var fps = Ui.Text(track.transform, "FPS", $"{r.averageFps:0} FPS", Theme.FontSmall, Color.white, Theme.Medium);
            fps.rectTransform.Fill(10, 0, 0, 0);
            var detail = $"1% low {r.lowFps:0}   ·   CPU {r.cpuMs:0.0} ms" + (r.hasGpuTime ? $"   ·   GPU {r.gpuMs:0.0} ms" : "");
            Ui.Text(row, "Detail", detail, Theme.FontSmall, Theme.TextDim).Size(270f, 44f);
        }

        // ------------------------------------------------------------------ state

        private void ApplySettings(GraphicsSettingsData settings, bool display) {
            var before = GraphicsSettingsManager.Current.Clone();
            GraphicsSettingsManager.Apply(settings);
            IdleFrameRate.Wake();
#if !UNITY_EDITOR
            if (display) KeepDisplayPrompt.Ask(before);
#endif
        }

        private void OnChanged(GraphicsSettingsData settings) => Refresh();

        private void Refresh() {
            var s = GraphicsSettingsManager.Current;
            if (s == null) return;
            foreach (var row in rows) {
                var index = Mathf.Clamp(row.option.get(s), 0, row.option.choices.Length - 1);
                var why = row.option.disabledWhy?.Invoke(s);
                row.value.text = row.option.choices[index];
                row.note.text = why ?? "";
                row.note.gameObject.SetActive(why != null);
                row.group.alpha = why != null ? 0.55f : 1f;
                row.left.interactable = why == null && index > 0;
                row.right.interactable = why == null && index < row.option.choices.Length - 1;
                for (var i = 0; i < row.pips.Length; i++)
                    row.pips[i].color = i == index ? Theme.Accent : Theme.Hover;
            }
            if (scaleSlider != null) {
                var was = rebuilding;
                rebuilding = true;
                scaleSlider.SetValueWithoutNotify(Mathf.Round(s.renderScale * 20f));
                rebuilding = was;
                scaleValue.text = Mathf.RoundToInt(s.renderScale * 100f) + "%";
            }
            foreach (var tile in tiles) {
                var selected = tile.preset == s.preset;
                tile.background.color = selected ? Theme.Accent : Theme.PanelRaised;
                tile.name.color = selected ? Color.white : Theme.Text;
                for (var i = 0; i < tile.meter.Length; i++)
                    tile.meter[i].color = i <= (int)tile.preset ? (selected ? Color.white : Theme.TextDim) : (selected ? new Color(1f, 1f, 1f, 0.3f) : Theme.Field);
                tile.badge.color = selected ? Color.white : Theme.Success;
            }
            if (presetState != null)
                presetState.text = s.preset == GraphicsPreset.Custom
                    ? "Custom: your own mix of settings. Pick a preset to start over from it."
                    : "Pick a preset, then change any setting below to fine-tune it.";
            if (hovered != null) ShowInfo(hovered);
        }

        private void ShowInfo(Option option) {
            hovered = option;
            if (option == null) {
                ShowSystemInfo();
                return;
            }
            var s = GraphicsSettingsManager.Current;
            infoTitle.text = option.title;
            var why = s != null ? option.disabledWhy?.Invoke(s) : null;
            infoValue.text = option.title == "Render scale" && s != null
                ? Mathf.RoundToInt(s.renderScale * 100f) + "%"
                : s != null ? "Now: " + option.choices[Mathf.Clamp(option.get(s), 0, option.choices.Length - 1)] : "";
            if (why != null) infoValue.text += "  ·  " + why;
            impactRow.SetActive(true);
            SetImpact(option.impact);
            infoAffects.text = "Affects: " + option.affects;
            infoBody.text = option.description;
        }

        private void ShowPresetInfo(GraphicsPreset preset) {
            hovered = null;
            var probe = new GraphicsSettingsData();
            probe.SetQualityFromPreset(preset);
            infoTitle.text = GraphicsBenchmark.PresetName(preset);
            infoValue.text = preset == Recommended() ? "Recommended for this PC" : "";
            impactRow.SetActive(true);
            SetImpact(Mathf.Clamp((int)preset - 1, 0, 4));
            infoAffects.text = preset switch {
                GraphicsPreset.VeryLow => "For integrated graphics and old workstation cards",
                GraphicsPreset.Low => "For old or entry-level graphics cards",
                GraphicsPreset.Medium => "For most graphics cards",
                GraphicsPreset.High => "For gaming graphics cards",
                GraphicsPreset.Ultra => "For fast graphics cards",
                _ => "For the fastest graphics cards",
            };
            var result = GraphicsBenchmark.Last?.FirstOrDefault(r => r.preset == preset);
            infoBody.text =
                $"Shadows: {probe.shadows}\nAnti-aliasing: {AaName(probe.antiAliasing)}\nAmbient occlusion: {probe.ambientOcclusion}\n" +
                $"Render scale: {Mathf.RoundToInt(probe.renderScale * 100f)}%\nTextures: {(probe.textures == TextureQuality.Full ? "High" : probe.textures == TextureQuality.Half ? "Medium" : "Low")}" +
                (result.HasValue && result.Value.frames > 0 ? $"\n\nBenchmark: {result.Value.averageFps:0} FPS (1% low {result.Value.lowFps:0})" : "");
        }

        private void ShowSystemInfo() {
            infoTitle.text = "This PC";
            infoValue.text = "Recommended: " + GraphicsBenchmark.PresetName(Recommended());
            impactRow.SetActive(false);
            infoAffects.text = GraphicsHardwareDetector.Describe(SystemInfo.graphicsDeviceName);
            infoBody.text = $"{SystemInfo.graphicsDeviceName}\n{SystemInfo.processorType.Trim()}\n{SystemInfo.systemMemorySize / 1024f:0} GB memory\n\n" +
                            "Point at a setting to see what it does and what it costs.\n\n" +
                            "Not sure? Run the Benchmark: it measures every preset on this PC with the build you have open.";
        }

        private void SetImpact(int impact) {
            for (var i = 0; i < impactBars.Length; i++)
                impactBars[i].color = i < impact ? (impact >= 3 ? Theme.Warning : Theme.Success) : Theme.Hover;
            if (impact == 0) impactBars[0].color = Theme.Hover;
        }

        private void UseRecommended() {
            var preset = Recommended();
            GraphicsSettingsManager.SetPreset(preset);
            IdleFrameRate.Wake();
            Interface.Say("Graphics preset: " + GraphicsBenchmark.PresetName(preset) + " (recommended)");
        }

        private void Revert() {
            GraphicsSettingsManager.Apply(opened);
            IdleFrameRate.Wake();
            Interface.Say("Graphics settings put back");
        }

        private void Update() {
            smoothed = Mathf.Lerp(smoothed, Time.unscaledDeltaTime, 0.1f);
            if (Time.unscaledTime < nextFpsUpdate || fpsText == null) return;
            nextFpsUpdate = Time.unscaledTime + 0.25f;
            var fps = 1f / Mathf.Max(smoothed, 0.0001f);
            fpsText.text = $"{fps:0} FPS";
            var s = GraphicsSettingsManager.Current;
            frameText.text = $"{smoothed * 1000f:0.0} ms per frame" + (s != null && s.vSync ? "  ·  V-Sync on" : "") +
                             (GraphicsSettingsManager.IdleLimit > 0 ? "  ·  idle" : "") +
                             (SmallPartCulling.Hidden > 0 ? $"  ·  {SmallPartCulling.Hidden} tiny parts hidden" : "");
        }

        // ------------------------------------------------------------------ benchmark

        private void StartBenchmark() {
            if (GraphicsBenchmark.Running) return;
            dialog.gameObject.SetActive(false);
            BenchmarkProgress.Show();
            GraphicsBenchmark.Run(new GraphicsBenchmark.Options { seconds = 5f });
            Interface.Say("Benchmark started: Esc stops it");
        }

        private void OnBenchmarkFinished(List<GraphicsBenchmark.Result> results) {
            if (this == null) return;
            dialog.gameObject.SetActive(true);
            ShowTab(Tab.Benchmark);
            var pick = BenchmarkPick();
            Interface.Say(pick.HasValue ? "Benchmark done: " + GraphicsBenchmark.PresetName(pick.Value) + " runs best here" : "Benchmark stopped");
        }

        /// <summary>The best preset the last benchmark ran smoothly: 60 FPS on average with its slow frames over 40, else 30/20.</summary>
        private static GraphicsPreset? BenchmarkPick() {
            var results = GraphicsBenchmark.Last;
            if (results == null || results.Count == 0) return null;
            foreach (var (average, low) in new[] { (60f, 40f), (30f, 20f) }) {
                var smooth = results.Where(r => r.averageFps >= average && r.lowFps >= low).ToList();
                // Extreme only when it's nearly free (it's 2.25x the pixels for sharpness alone).
                var ok = smooth.Where(r => r.preset != GraphicsPreset.Extreme || r.averageFps >= 100f).ToList();
                if (ok.Count > 0) return ok.Max(r => r.preset);
            }
            return GraphicsPreset.VeryLow;
        }

        private static GraphicsPreset Recommended() => BenchmarkPick() ?? GraphicsHardwareDetector.RecommendPreset();

        private static string Verdict(List<GraphicsBenchmark.Result> results) {
            var pick = BenchmarkPick() ?? GraphicsPreset.VeryLow;
            var chosen = results.First(r => r.preset == pick);
            var cpuBound = results.All(r => !r.hasGpuTime || r.gpuMs < r.frameMs * 0.7f);
            var text = $"Best here: {GraphicsBenchmark.PresetName(pick)}, {chosen.averageFps:0} FPS on average.";
            if (results.Last().averageFps > results.First().averageFps * 0.8f && cpuBound)
                text += " Every preset runs about as fast: the processor sets the pace here, not the graphics card, so the graphics settings change little. A smaller build (or fewer parts on screen) helps more.";
            else if (pick == GraphicsPreset.VeryLow && chosen.averageFps < 30f)
                text += GraphicsSettingsManager.Current != null && GraphicsSettingsManager.Current.smallPartCulling == 0
                    ? " Even Very Low is under 30 FPS: turn on Hide tiny far-away parts (Quality tab), then run it again."
                    : " Even Very Low is under 30 FPS: try a render scale of 50-70% (Display tab) and close other programs.";
            return text;
        }

        private static Color FpsColor(GraphicsBenchmark.Result r) =>
            r.averageFps >= 60f && r.lowFps >= 40f ? Theme.Success : r.averageFps >= 30f ? Theme.Warning : Theme.Danger;

        // ------------------------------------------------------------------ choices

        private static readonly int[] FrameLimits = { 0, 30, 60, 75, 90, 120, 144, 165, 240 };

        private static Option WindowModeOption() {
            var modes = new[] { FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen, FullScreenMode.Windowed };
            return new Option {
                title = "Window mode", impact = 0, affects = "Display", display = true,
                description = "Borderless: fills the screen and switches to other windows instantly. Fullscreen: has the screen to itself (can be a little faster on old PCs). Windowed: a normal window.",
                choices = new[] { "Borderless", "Fullscreen", "Windowed" },
                get = s => Mathf.Max(0, Array.IndexOf(modes, s.windowMode)), set = (s, i) => s.windowMode = modes[i],
                disabledWhy = _ => Application.isEditor ? "Set by the Game view in the editor" : null,
            };
        }

        private static Option ResolutionOption() {
            var list = Screen.resolutions.Select(r => (r.width, r.height)).Distinct()
                .Where(r => r.width >= 1024 && r.height >= 600).OrderByDescending(r => r.width * r.height).ToList();
            var native = (Display.main.systemWidth, Display.main.systemHeight);
            list.RemoveAll(r => r == native);
            var choices = new List<string> { $"Native ({native.Item1} x {native.Item2})" };
            choices.AddRange(list.Select(r => $"{r.Item1} x {r.Item2}"));
            return new Option {
                title = "Resolution", impact = 4, affects = "Graphics card (pixels)", display = true,
                description = "The window's size in pixels. Native is sharpest. To draw the 3D view at a lower resolution while the interface stays sharp, lower the render scale instead.",
                choices = choices.ToArray(),
                get = s => {
                    if (s.resolutionWidth <= 0) return 0;
                    var i = list.IndexOf((s.resolutionWidth, s.resolutionHeight));
                    return i < 0 ? 0 : i + 1;
                },
                set = (s, i) => {
                    s.resolutionWidth = i == 0 ? 0 : list[i - 1].Item1;
                    s.resolutionHeight = i == 0 ? 0 : list[i - 1].Item2;
                },
                disabledWhy = _ => Application.isEditor ? "Set by the Game view in the editor" : null,
            };
        }

        private static string AaName(AntiAliasing aa) => aa switch {
            AntiAliasing.MSAA2x => "MSAA 2x",
            AntiAliasing.MSAA4x => "MSAA 4x",
            AntiAliasing.MSAA8x => "MSAA 8x",
            _ => aa.ToString(),
        };

        private static int Closest(int[] values, int value) {
            var best = 0;
            for (var i = 1; i < values.Length; i++)
                if (Mathf.Abs(values[i] - value) < Mathf.Abs(values[best] - value)) best = i;
            return best;
        }

        // ------------------------------------------------------------------ helpers

        // Hover (info panel), wheel (step) and release (the slider's message) on a row.
        private class RowHover : MonoBehaviour, IPointerEnterHandler, IScrollHandler, IPointerUpHandler {
            public Action enter, release;
            public Action<int> wheel;

            public void OnPointerEnter(PointerEventData eventData) => enter?.Invoke();
            public void OnPointerUp(PointerEventData eventData) => release?.Invoke();

            public void OnScroll(PointerEventData eventData) {
                if (wheel == null || Mathf.Abs(eventData.scrollDelta.y) < 0.01f) {
                    // Let the list scroll.
                    ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, eventData, ExecuteEvents.scrollHandler);
                    return;
                }
                wheel(eventData.scrollDelta.y > 0 ? 1 : -1);
            }
        }
    }

    // V2: the card shown while the benchmark runs (the settings window hides so the build can be seen).
    public class BenchmarkProgress : MonoBehaviour {
        private TextMeshProUGUI label;
        private RectTransform fill;

        public static void Show() {
            var card = Ui.Panel(Interface.Root, "Benchmark Progress", Theme.Panel, 10f).rectTransform;
            card.SetAsLastSibling();
            card.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 90f), new Vector2(460f, 78f));
            var progress = card.gameObject.AddComponent<BenchmarkProgress>();
            progress.label = Ui.Text(card, "Label", "Starting…", Theme.FontBody, Theme.Text, Theme.Medium);
            progress.label.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -12f), new Vector2(300f, 26f));
            var hint = Ui.Hint(card, "Esc", "stop");
            hint.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -14f), new Vector2(80f, 22f));
            var track = Ui.Image(card, "Track", Theme.Field, 3f);
            track.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(424f, 8f));
            progress.fill = Ui.Image(track.transform, "Fill", Theme.Accent, 3f).rectTransform;
            progress.fill.anchorMin = Vector2.zero;
            progress.fill.anchorMax = new Vector2(0f, 1f);
            progress.fill.offsetMin = progress.fill.offsetMax = Vector2.zero;
            GraphicsBenchmark.Progress += progress.OnProgress;
            GraphicsBenchmark.Finished += progress.OnFinished;
        }

        private void OnProgress(string message, float amount) {
            if (this == null) return;
            label.text = "Testing " + message;
            fill.anchorMax = new Vector2(Mathf.Clamp01(amount), 1f);
        }

        private void OnFinished(List<GraphicsBenchmark.Result> results) {
            if (this != null) Destroy(gameObject);
        }

        private void OnDestroy() {
            GraphicsBenchmark.Progress -= OnProgress;
            GraphicsBenchmark.Finished -= OnFinished;
        }

        private void Update() {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                GraphicsBenchmark.Cancel();
        }
    }

    // V2: after the window mode or resolution changes (in a build), the old one comes back unless kept: a wrong
    // resolution can leave the screen black.
    public class KeepDisplayPrompt : MonoBehaviour {
        private GraphicsSettingsData before;
        private Dialog dialog;
        private TextMeshProUGUI text;
        private float until;

        public static void Ask(GraphicsSettingsData before) {
            var dialog = Dialog.Open("Keep these display settings?", new Vector2(460f, 200f));
            var prompt = dialog.gameObject.AddComponent<KeepDisplayPrompt>();
            prompt.before = before;
            prompt.dialog = dialog;
            prompt.until = Time.unscaledTime + 15f;
            prompt.text = Ui.Text(dialog.Body, "Text", "", Theme.FontBody, Theme.TextDim);
            prompt.text.rectTransform.Fill();
            prompt.text.textWrappingMode = TextWrappingModes.Normal;
            var back = Ui.Button(dialog.Footer, "Back", "Go back", prompt.GoBack, false, 6f);
            back.Size(-1f, 36f);
            var keep = Ui.Button(dialog.Footer, "Keep", "Keep", () => {
                Interface.Say("Display settings kept");
                prompt.before = null;
                dialog.Close();
            }, true, 6f);
            keep.Size(-1f, 36f);
            dialog.OnClose += () => { if (prompt.before != null) prompt.GoBack(); };
        }

        private void GoBack() {
            if (before == null) return;
            var settings = GraphicsSettingsManager.Current.Clone();
            settings.windowMode = before.windowMode;
            settings.resolutionWidth = before.resolutionWidth;
            settings.resolutionHeight = before.resolutionHeight;
            before = null;
            GraphicsSettingsManager.Apply(settings);
            Interface.Say("Display settings put back", false);
            if (dialog != null) dialog.Close();
        }

        private void Update() {
            var left = until - Time.unscaledTime;
            text.text = $"The screen changed. Going back to the previous display settings in {Mathf.CeilToInt(Mathf.Max(0f, left))} seconds.";
            if (left <= 0f) GoBack();
        }
    }
}
