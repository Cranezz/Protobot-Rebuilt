using System.Globalization;
using System.Linq;
using Protobot.GraphicsQuality;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.Game {
    // V2: the game's windows, in the new interface's style: Play (pick the game, mode, robot and field detail), the
    // setup panel down the left (start position, legal-start checklist, saved starts, preload, alignment tool), the HUD
    // while a run is on (timer, live score, Match Loads, End run) and the results.
    public static class GameUi {
        private static RectTransform setup, hud, perf;
        private static Dialog results;

        // FPS, frame time and the physics' share, under the view cube while a game is open.
        private static void ShowPerf() {
            if (perf != null) return;
            perf = Ui.Rect("Game Performance", Interface.Interface.Root);
            perf.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20f, -(Layout.TopBarBottom + 150f)), new Vector2(260f, 44f));
            perf.gameObject.AddComponent<PerfReadout>();
        }

        private class PerfReadout : MonoBehaviour {
            private TextMeshProUGUI text;
            private Unity.Profiling.ProfilerRecorder physics;
            private float smoothed = 1f / 60f, next;

            private void Awake() {
                text = Ui.Text(transform, "Text", "", Theme.FontSmall, Theme.Text, Theme.Medium, TextAlignmentOptions.TopRight);
                text.rectTransform.Fill();
                text.outlineWidth = 0.2f;
                text.outlineColor = new Color32(0, 0, 0, 200);
                physics = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Physics, "Physics.Simulate", 15);
            }

            private void OnDestroy() => physics.Dispose();

            private void Update() {
                smoothed = Mathf.Lerp(smoothed, Time.unscaledDeltaTime, 0.1f);
                if (Time.unscaledTime < next) return;
                next = Time.unscaledTime + 0.25f;
                var fps = 1f / Mathf.Max(smoothed, 0.0001f);
                text.color = fps >= 55f ? Theme.Success : fps >= 28f ? Theme.Warning : Theme.Danger;
                var line = $"{fps:0} FPS  ·  {smoothed * 1000f:0.0} ms";
                if (physics.Valid && physics.Count > 0) {
                    double sum = 0;
                    for (var i = 0; i < physics.Count; i++) sum += physics.GetSample(i).Value;
                    line += $"\nphysics {sum / physics.Count / 1e6:0.0} ms a frame";
                }
                text.text = line;
            }
        }

        public static void CloseAll() {
            if (setup != null) Object.Destroy(setup.gameObject);
            if (hud != null) Object.Destroy(hud.gameObject);
            if (results != null) {
                results.OnClose = null;
                results.Close();
            }
            if (perf != null) Object.Destroy(perf.gameObject);
            setup = hud = perf = null;
            results = null;
        }

        // ------------------------------------------------------------------ Play

        // The launch window (the original's main menu) gets "Play game" under Open; the buttons below move up a little.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AddToLaunchWindow() {
            var launch = GameObject.Find("Launch Canvas");
            if (launch == null) return;
            var buttons = launch.GetComponentsInChildren<Button>(true);
            var open = buttons.FirstOrDefault(b => b.name == "Open Button");
            if (open == null || open.transform.parent.Find("Play Game Button") != null) return;
            var close = buttons.FirstOrDefault(b => b.name == "Close Button" && b.transform.parent.name == "Splash Screen");
            var play = Object.Instantiate(open.gameObject, open.transform.parent);
            play.name = "Play Game Button";
            ((RectTransform)play.transform).anchoredPosition = ((RectTransform)open.transform).anchoredPosition + new Vector2(0f, -25f);
            var label = play.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = "Play game";
            var icon = play.transform.Find("Icon");
            if (icon != null && icon.TryGetComponent(out Image image)) {
                image.sprite = Theme.Circle;
                image.color = Theme.Accent;
            }
            var button = play.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => {
                if (close != null) close.onClick.Invoke();
                OpenPlay();
            });
            var y = -100f;
            foreach (var name in new[] { "Report Bugs Button", "Patch Notes Button", "Credits" }) {
                var other = open.transform.parent.Find(name) as RectTransform;
                if (other == null) continue;
                other.anchoredPosition = new Vector2(other.anchoredPosition.x, y);
                y -= 20f;
            }
        }

        public static void OpenPlay() {
            if (SkillsGame.Active) return;
            var games = GameDefinition.All();
            if (games.Length == 0) {
                Interface.Interface.Say("No game is installed (Tools > Protobot > Build Game Definitions)", false);
                return;
            }
            var dialog = Dialog.Open("Play a game", new Vector2(660f, 470f));
            var body = dialog.Body;
            Ui.Column(body, 10f);
            var chosen = 0;
            var light = GraphicsSettingsManager.Current != null && GraphicsSettingsManager.Current.preset <= GraphicsPreset.Low;

            var intro = Ui.Text(body, "Intro", "Put your robot on a competition field and drive it. Robot Skills: one robot, one minute, as many points as you can.", Theme.FontBody, Theme.TextDim);
            intro.textWrappingMode = TextWrappingModes.Normal;
            intro.Size(-1, 44f);

            var gameRow = Dialog.Row(body, "Game", 40f, 130f);
            Popup.Choice(gameRow, games.Select(g => g.Name).ToArray(), 0, i => chosen = i, 380f);

            var modeRow = Dialog.Row(body, "Mode", 40f, 130f);
            var mode = Ui.Text(modeRow, "Mode", "Driving Skills · 1:00", Theme.FontBody, Theme.Text, Theme.Medium);
            mode.Size(-1, 40f);
            var later = Ui.Text(body, "Later", "Autonomous Coding Skills and matches against other players are coming later.", Theme.FontSmall, Theme.TextFaint);
            later.margin = new Vector4(138, 0, 0, 0);
            later.textWrappingMode = TextWrappingModes.Normal;
            later.Size(-1, 34f);

            var robotRow = Dialog.Row(body, "Robot", 40f, 130f);
            var manager = Object.FindAnyObjectByType<Builds.BuildsManager>();
            var count = Object.FindObjectsByType<SavedObject>(FindObjectsSortMode.None).Count(s => s.gameObject.activeInHierarchy && !s.gameObject.IsDeleted());
            var name = manager != null && !string.IsNullOrEmpty(manager.buildPath) ? System.IO.Path.GetFileNameWithoutExtension(manager.buildPath) : "This build (not saved)";
            var robot = Ui.Text(robotRow, "Robot", count > 0 ? $"{name} · {count} parts" : "Nothing built yet", Theme.FontBody, count > 0 ? Theme.Text : Theme.Danger, Theme.Medium);
            robot.Size(-1, 40f, 1f);
            var open = Ui.Button(robotRow, "Open", "Open a build…", () => {
                dialog.Close();
                if (manager != null) manager.OpenBuild();
                Interface.Interface.Say("Open your robot, then File > Play game… again", true);
            }, false, 6f);
            open.Size(-1, 34f);

            var fieldRow = Dialog.Row(body, "Field", 40f, 130f);
            Popup.Choice(fieldRow, new[] { "Full detail", "Light (faster)" }, light ? 1 : 0, i => light = i == 1, 220f);
            var fieldNote = Ui.Text(body, "Field Note", "Light draws a fifth of the triangles: pick it on integrated graphics and old cards.", Theme.FontSmall, Theme.TextFaint);
            fieldNote.margin = new Vector4(138, 0, 0, 0);
            fieldNote.textWrappingMode = TextWrappingModes.Normal;
            fieldNote.Size(-1, 34f);

            var cancel = Ui.Button(dialog.Footer, "Cancel", "Cancel", dialog.Close, false, 6f);
            cancel.Size(-1f, 36f);
            var start = Ui.Button(dialog.Footer, "Start", "Set up the field", () => {
                dialog.Close();
                SkillsGame.Open(games[chosen], light);
            }, true, 6f);
            start.Size(-1f, 36f);
            start.interactable = count > 0;
        }

        // ------------------------------------------------------------------ setup

        public static void ShowSetup(SkillsGame game) {
            CloseAll();
            var panel = Ui.Panel(Interface.Interface.Root, "Game Setup", Theme.Panel, 12f).rectTransform;
            setup = panel;
            panel.anchorMin = new Vector2(0f, 0f);
            panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 0.5f);
            panel.offsetMin = new Vector2(12f, 12f);
            panel.offsetMax = new Vector2(12f + 360f, -(Layout.TopBarBottom + 10f));
            var builder = panel.gameObject.AddComponent<SetupPanel>();
            builder.game = game;
            builder.Build();
            ShowPerf();
        }

        private class SetupPanel : MonoBehaviour {
            public SkillsGame game;
            private RectTransform content;
            private bool dirty;
            private string saveName = "Start 1";

            private void OnEnable() { if (game != null) game.Changed += MarkDirty; }
            private void OnDisable() { if (game != null) game.Changed -= MarkDirty; }
            private void MarkDirty() => dirty = true;

            private void LateUpdate() {
                if (dirty && !InputEvents.TextInput.IsTyping) {
                    dirty = false;
                    Build();
                }
            }

            public void Build() {
                game.Changed -= MarkDirty;
                game.Changed += MarkDirty;
                if (content != null) Destroy(content.gameObject);
                var rect = (RectTransform)transform;
                var frame = Ui.Rect("Frame", rect).Fill(1, 1, 1, 1);
                frame.gameObject.AddComponent<RectMask2D>();
                content = frame;
                var column = Ui.Rect("Column", frame);
                column.anchorMin = new Vector2(0f, 1f);
                column.anchorMax = new Vector2(1f, 1f);
                column.pivot = new Vector2(0.5f, 1f);
                column.sizeDelta = Vector2.zero;
                Ui.Column(column, 6f, new RectOffset(16, 16, 14, 16));
                column.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                var scroll = frame.gameObject.AddComponent<ScrollRect>();
                scroll.viewport = frame;
                scroll.content = column;
                scroll.horizontal = false;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = 30f;

                var title = Ui.Text(column, "Title", "Driving Skills", Theme.FontTitle + 4f, Theme.Text, Theme.Medium);
                title.Size(-1, 30f);
                var sub = Ui.Text(column, "Sub", $"{game.definition.Name} · {Clock(game.definition.skillsSeconds)}", Theme.FontSmall, Theme.TextDim);
                sub.Size(-1, 20f);

                // The robot.
                Dialog.Heading(column, "Robot");
                var fits = game.depth <= 18.02f && game.width <= 18.02f && game.height <= 18.02f;
                var size = Ui.Text(column, "Size", $"{game.buildName} · {SkillsGame.Number(game.depth)} × {SkillsGame.Number(game.width)} × {SkillsGame.Number(game.height)}\"", Theme.FontSmall, fits ? Theme.TextDim : Theme.Danger);
                size.Size(-1, 20f);
                var frontRow = Row(column, "Front", 34f);
                foreach (var name in SkillsGame.FrontNames) {
                    var selected = game.FrontName == name;
                    var b = Ui.Button(frontRow, name, name, () => game.SetFront(name), selected, 6f, Theme.FontSmall);
                    b.Size(48f, 30f);
                }
                Ui.Tip(frontRow, "The robot's front", null, "Which side of the build is its front (the yellow arrow points to it). Headings are the way the front faces.");

                // The start.
                Dialog.Heading(column, "Start position");
                var note = Ui.Text(column, "Frame", "Inches from the field's middle: X toward blue, Y away from the audience. Heading: degrees clockwise from +Y.", Theme.FontSmall - 1f, Theme.TextFaint);
                note.textWrappingMode = TextWrappingModes.Normal;
                note.Size(-1, 34f);
                NumberRow(column, "X", game.x, v => game.SetPose(v, game.y, game.heading), "\"");
                NumberRow(column, "Y", game.y, v => game.SetPose(game.x, v, game.heading), "\"");
                NumberRow(column, "Heading", game.heading, v => game.SetPose(game.x, game.y, v), "°");
                var nudges = Row(column, "Nudge", 32f);
                Nudge(nudges, "-X", () => game.Nudge(-0.25f, 0f, 0f));
                Nudge(nudges, "+X", () => game.Nudge(0.25f, 0f, 0f));
                Nudge(nudges, "-Y", () => game.Nudge(0f, -0.25f, 0f));
                Nudge(nudges, "+Y", () => game.Nudge(0f, 0.25f, 0f));
                Nudge(nudges, "-5°", () => game.Nudge(0f, 0f, -5f));
                Nudge(nudges, "+5°", () => game.Nudge(0f, 0f, 5f));
                var hint = Ui.Hint(column, "Arrows", "1/4\" (Shift 1/16\")", "Q/E", "5° (Shift 0.5°)");
                hint.Size(-1, 22f);
                var tools = Row(column, "Tools", 34f);
                var back = Ui.Button(tools, "Back up", "Back up to contact", game.BackUp, false, 6f, Theme.FontSmall);
                back.Size(-1, 30f);
                Ui.Tip(back, "Back up to contact", null, "Slides the robot straight back until it touches the wall, a Goal or the alignment tool.");
                var reset = Ui.Button(tools, "Default", "Default start", () => { game.DefaultStart(); Interface.Interface.Say("Default start: against the red wall, facing the field", true); }, false, 6f, Theme.FontSmall);
                reset.Size(-1, 30f);

                // Saved starts.
                Dialog.Heading(column, "Saved starts");
                var names = game.StartNames();
                if (names.Count > 0) {
                    var useRow = Row(column, "Use", 34f);
                    Popup.Choice(useRow, names.ToArray(), -1, i => game.UseStart(names[i]), 200f);
                    var del = Ui.Button(useRow, "Delete", "Delete…", () => Popup.Open((RectTransform)useRow, names.ToArray(), -1, i => game.DeleteStart(names[i])), false, 6f, Theme.FontSmall);
                    del.Size(-1, 30f);
                }
                var saveRow = Row(column, "Save", 34f);
                var field = Ui.Field(saveRow, "Name", saveName, v => saveName = string.IsNullOrWhiteSpace(v) ? saveName : v.Trim(), Theme.FontSmall);
                field.Size(180f, 30f);
                var save = Ui.Button(saveRow, "Save", "Save this start", () => game.SaveStart(saveName), false, 6f, Theme.FontSmall);
                save.Size(-1, 30f);

                // Preload and alignment tool.
                Dialog.Heading(column, "Preload");
                var preloadRow = Row(column, "Preload", 34f);
                var pre = Ui.Button(preloadRow, "Preload", game.preload ? "Red/yellow pin in front: on" : "Preload: off", () => game.SetPreload(!game.preload), game.preload, 6f, Theme.FontSmall);
                pre.Size(-1, 30f);

                Dialog.Heading(column, "Alignment tool");
                if (game.tool == null) {
                    var load = Ui.Button(column, "Load tool", "Load a tool (a build file)…", game.LoadTool, false, 6f, Theme.FontSmall);
                    load.Size(-1, 32f);
                    var toolNote = Ui.Text(column, "Tool Note", "Build your alignment jig (a few C-channels) as its own build, load it here, place it, then Back up to it. It's removed when the run starts, like a real one.", Theme.FontSmall - 1f, Theme.TextFaint);
                    toolNote.textWrappingMode = TextWrappingModes.Normal;
                    toolNote.Size(-1, 50f);
                }
                else {
                    var toolRow = Row(column, "Tool", 32f);
                    var toolName = Ui.Text(toolRow, "Name", game.toolName, Theme.FontSmall, Theme.Text, Theme.Medium);
                    toolName.Size(-1, 30f, 1f);
                    var remove = Ui.Button(toolRow, "Remove", "Remove", () => game.RemoveTool(), false, 6f, Theme.FontSmall);
                    remove.Size(-1, 30f);
                    NumberRow(column, "Tool X", game.toolX, v => game.SetTool(v, game.toolY, game.toolHeading), "\"");
                    NumberRow(column, "Tool Y", game.toolY, v => game.SetTool(game.toolX, v, game.toolHeading), "\"");
                    NumberRow(column, "Tool heading", game.toolHeading, v => game.SetTool(game.toolX, game.toolY, v), "°");
                }

                // The checklist.
                Dialog.Heading(column, "Legal start (SG1, RSC2)");
                foreach (var (rule, ok) in game.Checks()) {
                    var check = Row(column, "Check", 22f);
                    var dot = Ui.Image(check, "Dot", ok ? Theme.Success : Theme.Danger);
                    dot.sprite = Theme.Circle;
                    dot.Size(10f, 10f);
                    var line = Ui.Text(check, "Rule", rule, Theme.FontSmall, ok ? Theme.Text : Theme.Danger);
                    line.Size(-1, 22f, 1f);
                }

                var buttons = Row(column, "Buttons", 46f);
                buttons.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(0, 0, 10, 0);
                var start = Ui.Button(buttons, "Start", "Start run", game.StartRun, true, 7f, Theme.FontBody + 1f);
                start.Size(170f, 40f);
                var exit = Ui.Button(buttons, "Exit", "Exit game", game.Exit, false, 7f);
                exit.Size(-1, 40f);
            }

            private static RectTransform Row(RectTransform parent, string name, float height) {
                var row = Ui.Rect(name, parent);
                Ui.Row(row, 6f);
                row.Size(-1, height);
                return row;
            }

            private static void Nudge(RectTransform row, string label, System.Action action) {
                var b = Ui.Button(row, label, label, action, false, 5f, Theme.FontSmall);
                b.Size(44f, 30f);
            }

            private static void NumberRow(RectTransform parent, string label, float value, System.Action<float> set, string unit) {
                var row = Dialog.Row(parent, label, 34f, 110f);
                var field = Ui.Field(row, "Value", SkillsGame.Number(value), text => {
                    var t = text.Trim().TrimEnd('"', '°', ' ').Replace(" in", "");
                    if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || Inch.TryParse(t, out v)) set(v);
                    else Interface.Interface.Say("That isn't a number", false);
                }, Theme.FontBody);
                field.Size(140f, 30f);
                var u = Ui.Text(row, "Unit", unit, Theme.FontBody, Theme.TextDim);
                u.Size(20f, 30f);
            }
        }

        // ------------------------------------------------------------------ the run

        public static void ShowHud(SkillsGame game) {
            CloseAll();
            var root = Ui.Rect("Game HUD", Interface.Interface.Root).Fill();
            hud = root;
            var h = root.gameObject.AddComponent<Hud>();
            h.game = game;
            h.Build();
            ShowPerf();
        }

        private class Hud : MonoBehaviour {
            public SkillsGame game;
            private TextMeshProUGUI clock, scoreText, details, countdown, loadsTitle;
            private readonly System.Collections.Generic.Dictionary<string, TextMeshProUGUI> loadLabels = new();
            private Button loaderButton;

            public void Build() {
                var card = Ui.Panel(transform, "Score Card", Theme.Panel, 12f).rectTransform;
                card.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -(Layout.TopBarBottom + 10f)), new Vector2(420f, 120f));
                clock = Ui.Text(card, "Clock", "1:00", 44f, Theme.Text, Theme.Bold, TextAlignmentOptions.Center);
                clock.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -10f), new Vector2(150f, 56f));
                scoreText = Ui.Text(card, "Score", "0", 44f, Theme.Accent, Theme.Bold, TextAlignmentOptions.Center);
                scoreText.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-14f, -10f), new Vector2(150f, 56f));
                var label = Ui.Text(card, "Label", "TIME            SCORE", 11f, Theme.TextFaint, Theme.Medium, TextAlignmentOptions.Center);
                label.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(120f, 18f));
                details = Ui.Text(card, "Details", "", Theme.FontSmall, Theme.TextDim, null, TextAlignmentOptions.Top);
                details.textWrappingMode = TextWrappingModes.Normal;
                details.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(392f, 48f));

                // Match Loads.
                var loads = Ui.Panel(transform, "Match Loads", Theme.Panel, 12f).rectTransform;
                loads.Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 14f), new Vector2(300f, 196f));
                var column = Ui.Rect("Column", loads).Fill(14, 12, 14, 12);
                Ui.Column(column, 6f);
                loadsTitle = Ui.Text(column, "Title", "Match Loads (red Loaders)", Theme.FontBody, Theme.Text, Theme.Medium);
                loadsTitle.Size(-1, 22f);
                foreach (var kind in new[] { "Red-Neutral", "Blue-Neutral", "Cup" }) {
                    var b = Ui.Button(column, kind, null, () => game.DropMatchLoad(kind), false, 6f, Theme.FontSmall);
                    b.Size(-1, 32f);
                    var text = Ui.Text(b.transform, "Text", "", Theme.FontSmall, Theme.Text, Theme.Medium, TextAlignmentOptions.Center);
                    text.rectTransform.Fill();
                    loadLabels[kind] = text;
                }
                loaderButton = Ui.Button(column, "Loader", null, () => { game.loader = 1 - game.loader; Refresh(); Interface.Interface.Say("Match Loads go into the " + (game.loader == 0 ? "first" : "second") + " red Loader", true); }, false, 6f, Theme.FontSmall);
                loaderButton.Size(-1, 30f);
                var loaderText = Ui.Text(loaderButton.transform, "Text", "", Theme.FontSmall, Theme.TextDim, null, TextAlignmentOptions.Center);
                loaderText.rectTransform.Fill();

                // End / back.
                var end = Ui.Button(transform, "End", "End run (Esc)", () => game.EndRun(), false, 7f);
                ((RectTransform)end.transform).Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-14f, 14f), new Vector2(150f, 40f));
                var setupButton = Ui.Button(transform, "Setup", "Back to setup", game.BackToSetup, false, 7f);
                ((RectTransform)setupButton.transform).Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-174f, 14f), new Vector2(150f, 40f));

                countdown = Ui.Text(transform, "Countdown", "", 140f, Color.white, Theme.Bold, TextAlignmentOptions.Center);
                countdown.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(600f, 200f));
                countdown.outlineWidth = 0.15f;
                countdown.outlineColor = new Color32(0, 0, 0, 180);

                game.Changed += Refresh;
                Refresh();
            }

            private void OnDestroy() {
                if (game != null) game.Changed -= Refresh;
            }

            private void Refresh() {
                if (scoreText == null) return;
                var s = game.score;
                scoreText.text = s.total.ToString();
                details.text = s.details.Count > 0 ? string.Join("\n", s.details) : (game.state == SkillsGame.State.Countdown ? "Get ready…" : "Stack pins on goals: red halves in red Quadrants, blue in blue, yellow where your Toggle is set");
                foreach (var (kind, text) in loadLabels) {
                    game.matchLoads.TryGetValue(kind, out var left);
                    text.text = $"{char.ToUpper(SkillsGame.LoadName(kind)[0])}{SkillsGame.LoadName(kind).Substring(1)}  ({left} left)";
                }
                loaderButton.GetComponentInChildren<TextMeshProUGUI>().text = "Into the " + (game.loader == 0 ? "first" : "second") + " red Loader (click to switch)";
            }

            private void Update() {
                if (game == null || clock == null) return;
                clock.text = Clock(game.timeLeft);
                clock.color = game.state == SkillsGame.State.Running && game.timeLeft < 10f ? Theme.Danger : Theme.Text;
                if (game.state == SkillsGame.State.Countdown) countdown.text = Mathf.CeilToInt(game.countdown).ToString();
                else if (game.state == SkillsGame.State.Running && game.definition.skillsSeconds - game.timeLeft < 0.8f) countdown.text = "GO";
                else if (game.state == SkillsGame.State.Ending) countdown.text = "";
                else countdown.text = "";
            }
        }

        public static string Clock(float seconds) {
            var s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        // ------------------------------------------------------------------ results

        public static void ShowResults(SkillsGame game, bool newBest) {
            if (results != null) {
                results.OnClose = null;
                results.Close();
            }
            var dialog = Dialog.Open("Run complete", new Vector2(520f, 420f));
            results = dialog;
            var body = dialog.Body;
            Ui.Column(body, 8f);
            var total = Ui.Text(body, "Total", game.score.total.ToString(), 72f, Theme.Accent, Theme.Bold, TextAlignmentOptions.Center);
            total.overflowMode = TextOverflowModes.Overflow;   // a line taller than its box would be hidden whole
            total.Size(-1, 92f);
            var best = Ui.Text(body, "Best", newBest ? "New best for this robot!" : $"Best for this robot: {game.best}", Theme.FontBody, newBest ? Theme.Success : Theme.TextDim, Theme.Medium, TextAlignmentOptions.Center);
            best.Size(-1, 26f);
            Dialog.Heading(body, "Points");
            var lines = game.score.details.Count > 0 ? game.score.details : new System.Collections.Generic.List<string> { "No points this time" };
            foreach (var line in lines) {
                var t = Ui.Text(body, "Line", line, Theme.FontBody, Theme.Text);
                t.Size(-1, 24f);
            }
            var placed = Ui.Text(body, "Placed", $"{game.score.placedPins} pins and {game.score.placedCups} cups placed", Theme.FontSmall, Theme.TextFaint);
            placed.Size(-1, 22f);

            // Closing it (Esc, ×) goes back to setup.
            dialog.OnClose = () => {
                results = null;
                if (SkillsGame.Current == game && game.state == SkillsGame.State.Results) game.BackToSetup();
            };
            var exit = Ui.Button(dialog.Footer, "Exit", "Exit game", () => { dialog.OnClose = null; results = null; dialog.Close(); game.Exit(); }, false, 6f);
            exit.Size(-1f, 36f);
            var change = Ui.Button(dialog.Footer, "Setup", "Change start", dialog.Close, false, 6f);
            change.Size(-1f, 36f);
            var again = Ui.Button(dialog.Footer, "Again", "Run again", () => {
                dialog.OnClose = null;
                results = null;
                dialog.Close();
                game.StartRunNextFrame();
            }, true, 6f);
            again.Size(-1f, 36f);
        }
    }
}
