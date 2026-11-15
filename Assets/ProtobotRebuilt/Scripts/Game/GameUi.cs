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
        private static RectTransform setup, hud, perf, controls;
        private static Dialog results;

        // FPS, frame time and the physics' share, under the view cube while a game is open.
        // The robot's control layout, top right under the performance readout (saved with the build).
        private static void ShowControls(bool editable) {
            if (controls != null) Object.Destroy(controls.gameObject);
            controls = null;
            // V2 (Phase 9): in Programming Skills the keys don't drive (the program does): no key layout.
            if (SkillsGame.Current != null && SkillsGame.Current.mode == SkillsGame.Mode.Programming) return;
            controls = (RectTransform)Motion.ControlsCard.Create(Interface.Interface.Root, Layout.TopBarBottom + 200f,
                editable ? () => Devices.DevicesPanel.Open(true) : null).transform;
        }

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

        /// <summary>V2: the run's HUD goes when the score reveal starts.</summary>
        public static void HideHud() {
            if (hud != null) Object.Destroy(hud.gameObject);
            hud = null;
        }

        public static void CloseAll() {
            if (setup != null) Object.Destroy(setup.gameObject);
            if (hud != null) Object.Destroy(hud.gameObject);
            if (results != null) {
                results.OnClose = null;
                results.Close();
            }
            if (perf != null) Object.Destroy(perf.gameObject);
            if (controls != null) Object.Destroy(controls.gameObject);
            Devices.DevicesPanel.Close();
            setup = hud = perf = controls = null;
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
            // V2: Multiplayer under Play game (the online screen: server list, leaderboard, matches).
            var multiplayer = Object.Instantiate(open.gameObject, open.transform.parent);
            multiplayer.name = "Multiplayer Button";
            ((RectTransform)multiplayer.transform).anchoredPosition = ((RectTransform)open.transform).anchoredPosition + new Vector2(0f, -50f);
            var multiplayerLabel = multiplayer.GetComponentInChildren<TextMeshProUGUI>(true);
            if (multiplayerLabel != null) multiplayerLabel.text = "Multiplayer";
            var multiplayerButton = multiplayer.GetComponent<Button>();
            multiplayerButton.onClick = new Button.ButtonClickedEvent();
            multiplayerButton.onClick.AddListener(() => {
                if (close != null) close.onClick.Invoke();
                Online.OnlineWindow.Open();
            });
            // The recent builds list moves down to make room (it has space left at the bottom).
            if (open.transform.parent.parent.Find("Builds Menu") is RectTransform recent) recent.anchoredPosition += new Vector2(0f, -25f);
            var y = -125f;
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

            // V2: Robot Skills or a match (practice on your own), each with its own start, preload and timing.
            var modeRow = Dialog.Row(body, "Mode", 40f, 130f);
            var modeChoice = 0;
            // V2 (Phase 9): Programming Skills: the robot's program drives the whole minute.
            Popup.Choice(modeRow, new[] { "Driving Skills · 1:00", "Programming Skills · 1:00", "Match practice · 0:15 + 1:45" }, 0, i => modeChoice = i, 380f);
            var later = Ui.Text(body, "Later", "Programming Skills: the robot's program (Code in the top bar) drives the whole minute. Match practice starts like a match: one Quadrant on your Alliance's side, your Alliance's preload. Against other players: Multiplayer.", Theme.FontSmall, Theme.TextFaint);
            later.margin = new Vector4(138, 0, 0, 0);
            later.textWrappingMode = TextWrappingModes.Normal;
            later.Size(-1, 50f);

            var robotRow = Dialog.Row(body, "Robot", 40f, 130f);
            var manager = Object.FindAnyObjectByType<Builds.BuildsManager>();
            var count = Object.FindObjectsByType<SavedObject>(FindObjectsSortMode.None).Count(s => s.gameObject.activeInHierarchy && !s.gameObject.IsDeleted());
            var name = manager != null && !string.IsNullOrEmpty(manager.buildPath) ? System.IO.Path.GetFileNameWithoutExtension(manager.buildPath) : "This build (not saved)";
            var robot = Ui.Text(robotRow, "Robot", count > 0 ? $"{name} · {count} parts" : "Nothing built yet", Theme.FontBody, count > 0 ? Theme.Text : Theme.Danger, Theme.Medium);
            robot.Size(-1, 40f, 1f);
            var open = Ui.Button(robotRow, "Open", "Open a build…", () => {
                dialog.Close();
                if (manager != null) manager.OpenBuild();
                Interface.Interface.Say("Open your robot, then File > Play game… again", false);
            }, false, 6f);
            open.Size(-1, 34f);

            var fieldRow = Dialog.Row(body, "Field", 40f, 130f);
            Popup.Choice(fieldRow, new[] { "Full detail", "Light (faster)" }, light ? 1 : 0, i => light = i == 1, 220f);
            var fieldNote = Ui.Text(body, "Field Note", "Light draws a fifth of the triangles: pick it on integrated graphics and old cards.", Theme.FontSmall, Theme.TextFaint);
            fieldNote.margin = new Vector4(138, 0, 0, 0);
            fieldNote.textWrappingMode = TextWrappingModes.Normal;
            fieldNote.Size(-1, 34f);

            // V2: online matches (Online/OnlineWindow).
            var online = Ui.Button(dialog.Footer, "Online", "Multiplayer…", () => {
                dialog.Close();
                Online.OnlineWindow.Open();
            }, false, 6f);
            online.Size(-1f, 36f);
            var cancel = Ui.Button(dialog.Footer, "Cancel", "Cancel", dialog.Close, false, 6f);
            cancel.Size(-1f, 36f);
            var start = Ui.Button(dialog.Footer, "Start", "Set up the field", () => {
                dialog.Close();
                SkillsGame.Open(games[chosen], light, modeChoice == 2 ? SkillsGame.Mode.Match : modeChoice == 1 ? SkillsGame.Mode.Programming : SkillsGame.Mode.Skills);
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
            ShowControls(true);
        }

        private class SetupPanel : MonoBehaviour {
            public SkillsGame game;
            private RectTransform content;
            private bool dirty;
            private string saveName = "Start 1";

            private void OnEnable() { if (game != null) game.Changed += MarkDirty; }
            private void OnDisable() { if (game != null) game.Changed -= MarkDirty; }
            private void MarkDirty() => dirty = true;

            private float built;

            private void LateUpdate() {
                // V2: at most ten times a second (dragging the robot changes it every frame).
                if (dirty && !InputEvents.TextInput.IsTyping && Time.unscaledTime - built > 0.1f) {
                    dirty = false;
                    Build();
                }
            }

            public void Build() {
                built = Time.unscaledTime;
                game.Changed -= MarkDirty;
                game.Changed += MarkDirty;
                // Rebuilt after every change (the wheel over a value changes it a step at a time): keep the scroll.
                var scrolled = content != null && content.childCount > 0 ? ((RectTransform)content.GetChild(0)).anchoredPosition.y : 0f;
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

                var title = Ui.Text(column, "Title", game.ModeName, Theme.FontTitle + 4f, Theme.Text, Theme.Medium);
                title.Size(-1, 30f);
                var sub = Ui.Text(column, "Sub", $"{game.definition.Name} · " + (game.SkillsRun ? Clock(game.definition.skillsSeconds) + (game.mode == SkillsGame.Mode.Programming ? " of robot code" : "") : "0:15 autonomous + 1:45 driver"), Theme.FontSmall, Theme.TextDim);
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
                // V2: a match: the Alliance and its Quadrant, and the autonomous period.
                if (game.mode == SkillsGame.Mode.Match && game.online) {   // V2: online, the lobby set them
                    Dialog.Heading(column, "Alliance and start");
                    var seat = Row(column, "Seat", 30f);
                    Ui.Text(seat, "Seat", (game.redAlliance ? "Red" : "Blue") + " Alliance · " + game.startQuadrant + " Quadrant (from the lobby)", Theme.FontSmall,
                        game.redAlliance ? new Color(0.95f, 0.45f, 0.45f) : new Color(0.45f, 0.65f, 1f), Theme.Medium).Size(-1, 30f, 1f);
                }
                else if (game.mode == SkillsGame.Mode.Match) {
                    Dialog.Heading(column, "Alliance and start");
                    var allianceRow = Row(column, "Alliance", 34f);
                    foreach (var red in new[] { true, false }) {
                        var picked = game.redAlliance == red;
                        var b = Ui.Button(allianceRow, red ? "Red" : "Blue", red ? "Red" : "Blue", () => game.SetAlliance(red), picked, 6f, Theme.FontSmall);
                        if (picked) b.targetGraphic.color = red ? new Color(0.85f, 0.25f, 0.25f) : new Color(0.2f, 0.45f, 0.9f);
                        b.Size(70f, 30f);
                    }
                    var quadrantRow = Row(column, "Quadrant", 34f);
                    foreach (var q in game.redAlliance ? new[] { Quadrant.Left, Quadrant.Bottom } : new[] { Quadrant.Right, Quadrant.Top }) {
                        var b = Ui.Button(quadrantRow, q.ToString(), q.ToString(), () => game.SetQuadrant(q), game.startQuadrant == q, 6f, Theme.FontSmall);
                        b.Size(80f, 30f);
                    }
                    Ui.Tip(quadrantRow, "Start Quadrant", null, "SG1: each robot starts in its own Quadrant on its Alliance's side of the Autonomous Line (the dashed diagonal), touching the Field Perimeter.");
                    ToolOptions.Switch(column, "Skip autonomous (robot code comes later)", game.skipAutonomous, game.SetSkipAutonomous);
                }

                // V2: the robot always touches its wall (SG1): where along it, and which way it's turned.
                Dialog.Heading(column, "Start position");
                var note = Ui.Text(column, "Frame", "Your robot starts touching the wall (SG1). Drag it on the field to slide it along the wall; drag the blue ring to turn it.", Theme.FontSmall - 1f, Theme.TextFaint);
                note.textWrappingMode = TextWrappingModes.Normal;
                note.Size(-1, 34f);
                NumberRow(column, "Along wall", () => game.Slide, game.SetSlide, "\"");
                // V2: how far the robot's footprint is from each end of its wall (teams measure from the corners).
                var (toNear, toFar) = game.CornerGaps();
                var gaps = Ui.Text(column, "Gaps", "Gap to the corners: " + Inch.Format(toNear) + " (−) · " + Inch.Format(toFar) + " (+)", Theme.FontSmall - 1f, Theme.TextDim);
                gaps.Size(-1, 20f);
                NumberRow(column, "Angle", () => game.Turn, game.SetTurn, "°");
                var nudges = Row(column, "Nudge", 32f);
                foreach (var (label, action) in new (string, System.Action)[] {
                             ("Slide −", () => { game.PushUndo(); game.SlideBy(-0.25f); }), ("Slide +", () => { game.PushUndo(); game.SlideBy(0.25f); }),
                             ("Turn −", () => { game.PushUndo(); game.TurnBy(-5f); }), ("Turn +", () => { game.PushUndo(); game.TurnBy(5f); }),
                         }) {
                    var b = Ui.Button(nudges, label, label, action, false, 5f, Theme.FontSmall);
                    b.Size(-1, 30f);
                }
                var hint = Ui.Hint(column, "Arrows", "slide", "Q/E", "turn", "Ctrl+Z", "undo");
                hint.Size(-1, 22f);
                var tools = Row(column, "Tools", 34f);
                var square = Ui.Button(tools, "Square", "Square to the wall", () => { game.PushUndo(); game.SetTurn(0f); }, false, 6f, Theme.FontSmall);
                square.Size(-1, 30f);
                var reset = Ui.Button(tools, "Default", "Default start", () => { game.PushUndo(); game.DefaultStart(); }, false, 6f, Theme.FontSmall);
                reset.Size(-1, 30f);

                // V2: Line up (a simple mate) and Measure, on the field.
                Dialog.Heading(column, "Line up and measure");
                var modes = Row(column, "Modes", 34f);
                var setupTools = game.setupTools;
                foreach (var (label, m, tip) in new[] {
                             ("Line up", SetupTools.Mode.LineUp, "Click a face or point on your robot, then a face on the alignment tool or the field: the robot goes against it (turning to face it when you picked a face), still touching its wall."),
                             ("Measure", SetupTools.Mode.Measure, "Click two points on the robot, the tool or the field: the distance, and how far apart they are along X, Y and up."),
                         }) {
                    var on = setupTools != null && setupTools.mode == m;
                    var b = Ui.Button(modes, label, label, () => setupTools?.SetMode(on ? SetupTools.Mode.Move : m), on, 6f, Theme.FontSmall);
                    b.Size(-1, 30f);
                    Ui.Tip(b, label, "Esc stops", tip);
                }
                if (setupTools != null) {
                    var instructions = Ui.Text(column, "Instructions", setupTools.Instructions, Theme.FontSmall - 1f, setupTools.mode == SetupTools.Mode.Move ? Theme.TextFaint : Theme.Accent);
                    instructions.textWrappingMode = TextWrappingModes.Normal;
                    instructions.Size(-1, 34f);
                    if (setupTools.Readout.Length > 0) {
                        var readout = Ui.Text(column, "Readout", setupTools.Readout, Theme.FontSmall, Theme.Dimension, Theme.Medium);
                        readout.textWrappingMode = TextWrappingModes.Normal;
                        readout.Size(-1, 34f);
                    }
                    if (!string.IsNullOrEmpty(setupTools.message)) {
                        var said = Ui.Text(column, "Message", setupTools.message, Theme.FontSmall - 1f, Theme.Warning);
                        said.textWrappingMode = TextWrappingModes.Normal;
                        said.Size(-1, 34f);
                    }
                }

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
                var pre = Ui.Button(preloadRow, "Preload", game.preload ? char.ToUpper(SkillsGame.LoadName(game.PreloadColors)[0]) + SkillsGame.LoadName(game.PreloadColors).Substring(1) + ": on" : "Preload: off", () => game.SetPreload(!game.preload), game.preload, 6f, Theme.FontSmall);
                pre.Size(-1, 30f);
                // V2: where it touches the robot (SG5).
                if (game.preload) {
                    var spotRow = Row(column, "Where", 34f);
                    foreach (SkillsGame.PreloadSpot spot in System.Enum.GetValues(typeof(SkillsGame.PreloadSpot))) {
                        var b = Ui.Button(spotRow, spot.ToString(), spot == SkillsGame.PreloadSpot.Top ? "On top" : spot.ToString(), () => game.SetPreloadSpot(spot), game.preloadSpot == spot, 6f, Theme.FontSmall - 1f);
                        b.Size(-1, 28f);
                    }
                    Ui.Tip(spotRow, "Preload", null, "SG5: one Pin of your Alliance's color, touching your robot (the dashed yellow outline) and nothing else.");
                }

                // V2: the alignment tool from a build file or a saved group; it appears beside the robot.
                Dialog.Heading(column, "Alignment tool");
                if (game.tool == null) {
                    var loadRow = Row(column, "Load", 34f);
                    var load = Ui.Button(loadRow, "Load tool", "Build file…", game.LoadTool, false, 6f, Theme.FontSmall);
                    load.Size(-1, 30f);
                    Button groups = null;
                    groups = Ui.Button(loadRow, "Groups", "Saved group…", () => {
                        var names = Builds.SavedGroups.Names();
                        if (names.Count == 0) {
                            Interface.Interface.Say("No saved groups yet: in build mode, right-click a part > Save group", false);
                            return;
                        }
                        Popup.Open((RectTransform)groups.transform, names.ToArray(), -1, i => game.LoadToolGroup(names[i]));
                    }, false, 6f, Theme.FontSmall);
                    groups.Size(-1, 30f);
                    var toolNote = Ui.Text(column, "Tool Note", "Build your jig as its own build (or save it as a group). It appears beside your robot against the wall: drag it to move it (it sits flat on a wall when it's near one), drag its ring to turn it, then Line up your robot against it. It's removed when the run starts, like a real one.", Theme.FontSmall - 1f, Theme.TextFaint);
                    toolNote.textWrappingMode = TextWrappingModes.Normal;
                    toolNote.Size(-1, 66f);
                }
                else {
                    var toolRow = Row(column, "Tool", 32f);
                    var toolName = Ui.Text(toolRow, "Name", game.toolName, Theme.FontSmall, Theme.Text, Theme.Medium);
                    toolName.Size(-1, 30f, 1f);
                    var beside = Ui.Button(toolRow, "Beside", "Beside robot", () => { game.PushUndo(); game.ToolBesideRobot(); }, false, 6f, Theme.FontSmall);
                    beside.Size(-1, 30f);
                    var remove = Ui.Button(toolRow, "Remove", "Remove", () => game.RemoveTool(), false, 6f, Theme.FontSmall);
                    remove.Size(-1, 30f);
                    NumberRow(column, "Tool X", () => game.toolX, v => game.SetTool(v, game.toolY, game.toolHeading), "\"");
                    NumberRow(column, "Tool Y", () => game.toolY, v => game.SetTool(game.toolX, v, game.toolHeading), "\"");
                    NumberRow(column, "Tool heading", () => game.toolHeading, v => game.SetTool(game.toolX, game.toolY, v), "°");
                }

                // The checklist.
                Dialog.Heading(column, game.SkillsRun ? "Legal start (SG1, RSC2)" : "Legal start (SG1, SG5)");
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
                if (game.online) {   // V2: an online match: Ready (the host starts once everyone is), or leave
                    if (game.onlineLocked) {
                        var wait = Ui.Text(buttons, "Waiting", "Ready: waiting for the other players…", Theme.FontBody, Theme.Success, Theme.Medium);
                        wait.Size(-1, 40f, 1f);
                    }
                    else {
                        var ready = Ui.Button(buttons, "Ready", "Ready", () => game.onlineReady?.Invoke(), true, 7f, Theme.FontBody + 1f);
                        ready.Size(170f, 40f);
                        Ui.Tip(ready, "Ready", null, "Your start is sent to the others and can't change; the match starts when everyone is ready.");
                    }
                    var leave = Ui.Button(buttons, "Leave", "Leave match", game.Exit, false, 7f);
                    leave.Size(-1, 40f);
                }
                else {
                var start = Ui.Button(buttons, "Start", "Start run", game.StartRun, true, 7f, Theme.FontBody + 1f);
                start.Size(170f, 40f);
                var exit = Ui.Button(buttons, "Exit", "Exit game", game.Exit, false, 7f);
                exit.Size(-1, 40f);
                }

                if (scrolled > 0f) {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(column);
                    column.anchoredPosition = new Vector2(0f, Mathf.Min(scrolled, Mathf.Max(0f, column.rect.height - frame.rect.height)));
                }
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

            // The wheel over a row steps it: inches by 1/4" (Shift 1/16", Ctrl 1"), degrees by 5° (Shift 0.5°, Ctrl 15°).
            private static void NumberRow(RectTransform parent, string label, System.Func<float> get, System.Action<float> set, string unit) {
                var value = get();
                var row = Dialog.Row(parent, label, 34f, 110f);
                var degrees = unit == "°";
                var hit = row.gameObject.AddComponent<Image>();   // the whole row takes the wheel, not just its box
                hit.color = Color.clear;
                row.gameObject.AddComponent<WheelStep>().step = direction => {
                    value = get();
                    var keyboard = UnityEngine.InputSystem.Keyboard.current;
                    var shift = keyboard != null && keyboard.shiftKey.isPressed;
                    var ctrl = keyboard != null && keyboard.ctrlKey.isPressed;
                    var step = degrees ? (shift ? 0.5f : ctrl ? 15f : 5f) : (shift ? 0.0625f : ctrl ? 1f : 0.25f);
                    set(value + direction * step);
                    Audio.Sounds.Play(Audio.Sounds.Tick);
                };
                Ui.Tip(row, label, null, degrees ? "Turn the wheel over it: 5° a step (Shift 0.5°, Ctrl 15°)" : "Turn the wheel over it: 1/4\" a step (Shift 1/16\", Ctrl 1\")");
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
            ShowControls(false);
        }

        private class Hud : MonoBehaviour {
            public SkillsGame game;
            private TextMeshProUGUI clock, scoreText, details, countdown, loadsTitle, label;
            private readonly System.Collections.Generic.Dictionary<string, TextMeshProUGUI> loadLabels = new();
            private Button loaderButton;

            public void Build() {
                var card = Ui.Panel(transform, "Score Card", Theme.Panel, 12f).rectTransform;
                card.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -(Layout.TopBarBottom + 10f)), new Vector2(420f, 120f));
                clock = Ui.Text(card, "Clock", "1:00", 44f, Theme.Text, Theme.Bold, TextAlignmentOptions.Center);
                clock.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -10f), new Vector2(150f, 56f));
                scoreText = Ui.Text(card, "Score", "0", 44f, Theme.Accent, Theme.Bold, TextAlignmentOptions.Center);
                scoreText.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-14f, -10f), new Vector2(150f, 56f));
                label = Ui.Text(card, "Label", "TIME            SCORE", 11f, Theme.TextFaint, Theme.Medium, TextAlignmentOptions.Center);
                label.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(120f, 18f));
                details = Ui.Text(card, "Details", "", Theme.FontSmall, Theme.TextDim, null, TextAlignmentOptions.Top);
                details.textWrappingMode = TextWrappingModes.Normal;
                details.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(392f, 48f));

                // Match Loads.
                var loads = Ui.Panel(transform, "Match Loads", Theme.Panel, 12f).rectTransform;
                loads.Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 14f), new Vector2(300f, 82f + 38f * game.matchLoads.Count));
                var column = Ui.Rect("Column", loads).Fill(14, 12, 14, 12);
                Ui.Column(column, 6f);
                loadsTitle = Ui.Text(column, "Title", "Match Loads (" + (game.Red ? "red" : "blue") + " Loaders)", Theme.FontBody, Theme.Text, Theme.Medium);
                loadsTitle.Size(-1, 22f);
                foreach (var kind in new System.Collections.Generic.List<string>(game.matchLoads.Keys)) {   // V2: Skills' or the match's
                    var b = Ui.Button(column, kind, null, () => game.DropMatchLoad(kind), false, 6f, Theme.FontSmall);
                    b.Size(-1, 32f);
                    var text = Ui.Text(b.transform, "Text", "", Theme.FontSmall, Theme.Text, Theme.Medium, TextAlignmentOptions.Center);
                    text.rectTransform.Fill();
                    loadLabels[kind] = text;
                }
                loaderButton = Ui.Button(column, "Loader", null, () => { game.loader = 1 - game.loader; Refresh(); Interface.Interface.Say("Match Loads go into the " + (game.loader == 0 ? "first" : "second") + (game.Red ? " red" : " blue") + " Loader", true); }, false, 6f, Theme.FontSmall);
                loaderButton.Size(-1, 30f);
                var loaderText = Ui.Text(loaderButton.transform, "Text", "", Theme.FontSmall, Theme.TextDim, null, TextAlignmentOptions.Center);
                loaderText.rectTransform.Fill();

                // End / back.
                if (game.online) {   // V2: an online match runs to its end; leaving leaves the robot switched off
                    var leave = Ui.Button(transform, "Leave", "Leave match", game.Exit, false, 7f);
                    ((RectTransform)leave.transform).Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-14f, 14f), new Vector2(150f, 40f));
                    var players = Ui.Button(transform, "Players", "Players…", Online.OnlineMatch.PlayersDialog, false, 7f);
                    ((RectTransform)players.transform).Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-174f, 14f), new Vector2(150f, 40f));
                    Ui.Tip(players, "Players", null, "Who's playing: report a player, or vote to kick one.");
                }
                else {
                var end = Ui.Button(transform, "End", "End run (Esc)", () => game.EndRun(), false, 7f);
                ((RectTransform)end.transform).Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-14f, 14f), new Vector2(150f, 40f));
                var setupButton = Ui.Button(transform, "Setup", "Back to setup", game.BackToSetup, false, 7f);
                ((RectTransform)setupButton.transform).Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-174f, 14f), new Vector2(150f, 40f));
                }

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
                scoreText.text = s.match ? $"<color=#E5484D>{s.red}</color><size=55%> – </size><color=#3E8CF0>{s.blue}</color>" : s.total.ToString();   // V2: a match: both Alliances
                details.text = s.details.Count > 0 ? string.Join("\n", s.details) : (game.state == SkillsGame.State.Countdown ? "Get ready…" : "Stack pins on goals: red halves in red Quadrants, blue in blue, yellow where your Toggle is set");
                foreach (var (kind, text) in loadLabels) {
                    game.matchLoads.TryGetValue(kind, out var left);
                    text.text = $"{char.ToUpper(SkillsGame.LoadName(kind)[0])}{SkillsGame.LoadName(kind).Substring(1)}  ({left} left)";
                }
                loaderButton.GetComponentInChildren<TextMeshProUGUI>().text = "Into the " + (game.loader == 0 ? "first" : "second") + (game.Red ? " red" : " blue") + " Loader (click to switch)";
            }

            private void Update() {
                if (game == null || clock == null) return;
                // V2: a match shows its period (autonomous counts its own 0:15).
                clock.text = Clock(game.InAutonomous ? game.timeLeft - SkillsGame.DriverSeconds : game.timeLeft);
                if (label != null) label.text = game.mode == SkillsGame.Mode.Programming ? "PROGRAM         SCORE" : game.mode == SkillsGame.Mode.Skills ? "TIME            SCORE" : game.InAutonomous ? "AUTONOMOUS        SCORE" : "DRIVER            SCORE";
                clock.color = game.state == SkillsGame.State.Running && game.timeLeft < 10f ? Theme.Danger : Theme.Text;
                if (game.state == SkillsGame.State.Countdown) countdown.text = Mathf.CeilToInt(game.countdown).ToString();
                else if (game.state == SkillsGame.State.Running && game.RunSeconds - game.timeLeft < 0.8f) countdown.text = game.InAutonomous || game.mode == SkillsGame.Mode.Programming ? "AUTO" : "GO";
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
