using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Protobot.Interface;
using Protobot.Motion;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Protobot.RobotCode {
    /// <summary>
    /// V2 (Phase 9, robot code): the robot's program, in a window like a small VS Code: line numbers, Python colors,
    /// auto-indent, Tab = 4 spaces, Ctrl+Z/Y, Ctrl+/ comments; Run (it starts the simulation if needed) in a period
    /// (Program, Autonomous 0:15, Skills 1:00, Driver 1:45); the output and the Brain's screen below; an error
    /// marks its line. Examples, and "Open in VS Code" (CodeStore keeps the file in sync).
    /// </summary>
    public class CodePanel : MonoBehaviour {
        private const string RectKey = "V2.CodePanelRect", PeriodKey = "V2.CodePeriod", TabKey = "V2.CodeTab";
        private const float FontSize = 15f;
        private static CodePanel open;

        private static readonly string[] PeriodLabels = { "Program", "Autonomous 0:15", "Skills auton 1:00", "Driver 1:45" };

        private RectTransform window, viewport, errorBar, outputContent, screenBox, graphBox, monitorBox;
        private TMP_InputField field;
        private TextMeshProUGUI highlight, gutter, outputText, status, subtitle;
        private RectTransform bottomArea, editorArea;
        private const float OutputHeight = 170f, ScreenTabHeight = 280f;
        private ScrollRect outputScroll;
        private Button runButton, stopButton, outputTab, screenTab, graphTab, monitorTab, pauseButton, stepButton;
        // V2: the Monitor tab (threads, lines, variables), the markers on the lines the threads are on, and the
        // console's text color (set_console_text_color, [31m).
        private readonly CodeMonitor monitor = new();
        private TextMeshProUGUI monitorText;
        private float monitorNext;
        private int tab;
        private readonly List<RectTransform> runBars = new();
        private string consoleColor;
        private int period;
        private bool showScreen;
        private readonly StringBuilder output = new();
        private bool outputDirty, screenDirty, highlightDirty = true;
        private int errorLine;
        private string pendingInsert;
        private bool pendingCloseBrace;
        private int validateFrame = -1, validateCount;
        private bool pastedTabs;
        private bool syncing;

        // Undo history of the text box.
        private readonly List<(string text, int caret)> undo = new(), redo = new();
        private float lastSnapshot;

        public static bool IsOpen => open != null;

        public static void Open() {
            if (open != null) {
                open.transform.SetAsLastSibling();
                return;
            }
            if (Interface.Interface.Root == null) return;
            var go = Ui.Rect("Code Panel", Interface.Interface.Root).Fill().gameObject;
            open = go.AddComponent<CodePanel>();
            open.Build();
        }

        public static void Close() {
            if (open == null) return;
            open.Commit();
            Destroy(open.gameObject);
            open = null;
        }

        public static void Toggle() {
            if (open != null) Close();
            else Open();
        }

        private void OnEnable() {
            RobotProgram.Printed += OnPrinted;
            RobotProgram.StateChanged += OnState;
            CodeStore.Changed += OnStoreChanged;
            BrainScreen.Changed += OnScreen;
        }

        private void OnDisable() {
            RobotProgram.Printed -= OnPrinted;
            RobotProgram.StateChanged -= OnState;
            CodeStore.Changed -= OnStoreChanged;
            BrainScreen.Changed -= OnScreen;
        }

        // ------------------------------------------------------------------ building

        private void Build() {
            // The panel itself lets clicks through outside its window.
            var rootRect = ((RectTransform)Interface.Interface.Root).rect;
            window = Ui.Panel(transform, "Window", Theme.Panel, 10f).rectTransform;
            window.anchorMin = window.anchorMax = window.pivot = new Vector2(0f, 1f);
            var saved = PlayerPrefs.GetString(RectKey, "").Split(',');
            if (saved.Length == 4 && saved.All(s => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _))) {
                var v = saved.Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                window.anchoredPosition = new Vector2(v[0], v[1]);
                window.sizeDelta = new Vector2(Mathf.Max(460f, v[2]), Mathf.Max(360f, v[3]));
            }
            else {
                window.anchoredPosition = new Vector2(72f, -(Layout.TopBarBottom + 10f));
                window.sizeDelta = new Vector2(640f, Mathf.Max(420f, rootRect.height - Layout.TopBarBottom - 60f));
            }
            var body = (RectTransform)window.GetChild(0);

            // Header (drag it to move the window).
            var header = Ui.Image(body, "Header", new Color(0f, 0f, 0f, 0f), 0f, true).rectTransform;
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.offsetMin = new Vector2(12f, -42f);
            header.offsetMax = new Vector2(-10f, -6f);
            Ui.Row(header, 6f);
            header.gameObject.AddComponent<Mover>().owner = this;
            var mark = Ui.Text(header, "Mark", "</>", Theme.FontBody, Theme.Accent, Theme.Bold, TextAlignmentOptions.Center);
            mark.Size(30f, 30f);
            var title = Ui.Text(header, "Title", "Robot code", Theme.FontTitle, Theme.Text, Theme.Medium);
            title.Size(96f, 30f);
            subtitle = Ui.Text(header, "Subtitle", "Python", Theme.FontSmall, Theme.TextDim);
            subtitle.Size(-1, 30f, 1f);
            var examples = Ui.Button(header, "Examples", "Examples", null, false, 5f, Theme.FontSmall);
            examples.Size(-1, 26f);
            Popup.Chevron(examples.transform);
            examples.transform.Find("Chevron").gameObject.AddComponent<LayoutElement>().ignoreLayout = true;   // (placed by hand)
            examples.GetComponent<HorizontalLayoutGroup>().padding.right = 24;
            examples.onClick.AddListener(() => Popup.Open((RectTransform)examples.transform, Examples.All.Select(e => e.name).ToArray(), -1, UseExample));
            Ui.Tip(examples, "Examples", null, "Starting programs: tank and arcade drive, an autonomous square, gyro turns, pneumatics, sensors, a PID loop. Replaces what's in the editor (Ctrl+Z brings it back).");
            // V2: the robot's configuration (ports, cartridges, drivetrain measured from the build), as VEXcode writes it.
            var devices = Ui.Button(header, "Insert devices", "Devices", InsertDevices, false, 5f, Theme.FontSmall);
            devices.Size(-1, 26f);
            Ui.Tip(devices, "Insert devices", null, "Writes the robot's configuration at the top of the program: every motor on its port with its cartridge and direction, the drive's motor groups and a drivetrain measured from your build (wheel size, track width, gear ratio), the sensors and pneumatics. Again after you change the robot: it replaces the old block.");
            var external = Ui.Button(header, "Open in VS Code", "VS Code", () => {
                Commit();
                CodeStore.OpenExternally();
            }, false, 5f, Theme.FontSmall);
            external.Size(-1, 26f);
            Ui.Tip(external, "Open in your own editor", null, "Opens the program in VS Code (or Notepad), with the vex API for completions. Saving there updates it here.");
            var help = Ui.Button(header, "Help", "?", ShowHelp, false, 5f, Theme.FontTitle);
            help.Size(28f, 26f);
            Ui.Tip(help, "How robot code works", null, "The API, the ports and the keys.");
            var close = Ui.Button(header, "Close", "×", Close, false, 5f, Theme.FontTitle);
            close.Size(28f, 26f);
            Ui.Tip(close, "Close", null, "Closes the code window (a running program keeps running).");

            // Run bar.
            var bar = Ui.Rect("Run Bar", body);
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.offsetMin = new Vector2(12f, -82f);
            bar.offsetMax = new Vector2(-12f, -46f);
            Ui.Row(bar, 8f);
            period = Mathf.Clamp(PlayerPrefs.GetInt(PeriodKey, 0), 0, PeriodLabels.Length - 1);
            var choice = Popup.Choice(bar, PeriodLabels, period, i => {
                period = i;
                PlayerPrefs.SetInt(PeriodKey, i);
            }, 170f);
            Ui.Tip(choice, "What to run", null, "Program: the whole program (a Competition runs its driver control).\nAutonomous: its autonomous for 15 s.\nSkills auton: its autonomous for 60 s (programming skills).\nDriver: its driver control for 1:45.");
            runButton = Ui.Button(bar, "Run", "Run", Run, true, 6f, Theme.FontBody);
            runButton.Size(-1, 32f);
            Ui.Tip(runButton, "Run", "F5", "Starts the program on the simulated robot (and the simulation, if it isn't running).");
            stopButton = Ui.Button(bar, "Stop", "Stop", () => RobotProgram.Stop("Stopped"), false, 6f, Theme.FontBody);
            stopButton.Size(-1, 32f);
            Ui.Tip(stopButton, "Stop the program", "Shift+F5", "Its motors stop; the simulation keeps going (the keys drive again).");
            pauseButton = Ui.Button(bar, "Pause", "Pause", TogglePause, false, 6f, Theme.FontBody);
            pauseButton.Size(-1, 32f);
            Ui.Tip(pauseButton, "Pause", "F6", "Stops time: the robot, the program and its timers hold still, so the Monitor shows where each thread is and what its variables hold.");
            stepButton = Ui.Button(bar, "Step", "Step", () => StepTime(Keyboard.current != null && Keyboard.current.shiftKey.isPressed ? 24 : 1), false, 6f, Theme.FontBody);
            stepButton.Size(-1, 32f);
            Ui.Tip(stepButton, "Step", "F10", "While paused: one physics step (1/240 s) of the robot and its program. Shift: 0.1 s.");
            status = Ui.Text(bar, "Status", "", Theme.FontSmall, Theme.TextDim);
            status.Size(-1, 32f, 1f);

            // Output (bottom).
            const float outputHeight = OutputHeight;
            var bottom = Ui.Rect("Bottom", body);
            bottom.anchorMin = Vector2.zero;
            bottom.anchorMax = new Vector2(1f, 0f);
            bottom.pivot = new Vector2(0.5f, 0f);
            bottom.offsetMin = new Vector2(12f, 12f);
            bottom.offsetMax = new Vector2(-12f, 12f + outputHeight);
            var tabs = Ui.Rect("Tabs", bottom);
            tabs.anchorMin = new Vector2(0f, 1f);
            tabs.anchorMax = new Vector2(1f, 1f);
            tabs.pivot = new Vector2(0.5f, 1f);
            tabs.offsetMin = new Vector2(0f, -26f);
            tabs.offsetMax = Vector2.zero;
            Ui.Row(tabs, 4f);
            outputTab = Ui.Button(tabs, "Output", "Output", () => SetTab(false), false, 5f, Theme.FontSmall);
            outputTab.Size(-1, 24f);
            screenTab = Ui.Button(tabs, "Brain screen", "Brain screen", () => SetTab(true), false, 5f, Theme.FontSmall);
            screenTab.Size(-1, 24f);
            graphTab = Ui.Button(tabs, "Graph", "Graph", () => SetTab(2), false, 5f, Theme.FontSmall);
            graphTab.Size(-1, 24f);
            Ui.Tip(graphTab, "Tuning graph", null, "The motors your program drives (speed or position against what it asked for) and anything it prints as name: number, over time. For tuning PID.");
            monitorTab = Ui.Button(tabs, "Monitor", "Monitor", () => SetTab(3), false, 5f, Theme.FontSmall);
            monitorTab.Size(-1, 24f);
            Ui.Tip(monitorTab, "Monitor", null, "While the program runs: each thread and the line it's on (click to open its calls and local variables), the variables and sensors monitor_variable() and monitor_sensor() ask for, and every global variable (click a name to pin it). Values that just changed are lit. Pause stops time to look; Step moves it on.");
            Ui.Rect("Space", tabs).gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var clear = Ui.Button(tabs, "Clear", "Clear", () => {
                output.Clear();
                outputDirty = true;
            }, false, 5f, Theme.FontSmall);
            clear.Size(-1, 24f);
            Ui.Tip(clear, "Clear the output");

            var outBox = Ui.Image(bottom, "Output Box", new Color(0.05f, 0.055f, 0.065f, 1f), 6f, true).rectTransform;
            outBox.Fill(0, 30, 0, 0);
            var outViewport = Ui.Rect("Viewport", outBox).Fill(8, 6, 8, 6);
            outViewport.gameObject.AddComponent<RectMask2D>();
            outputContent = Ui.Rect("Content", outViewport);
            outputContent.anchorMin = new Vector2(0f, 1f);
            outputContent.anchorMax = new Vector2(1f, 1f);
            outputContent.pivot = new Vector2(0.5f, 1f);
            outputContent.sizeDelta = Vector2.zero;
            Ui.Column(outputContent, 0f);
            outputContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            outputText = Ui.Text(outputContent, "Text", "", 13f, Theme.Text, MonoFont.Get);
            outputText.textWrappingMode = TextWrappingModes.Normal;
            outputText.overflowMode = TextOverflowModes.Overflow;
            outputText.alignment = TextAlignmentOptions.TopLeft;
            outputText.richText = true;
            outputScroll = outBox.gameObject.AddComponent<ScrollRect>();
            outputScroll.viewport = outViewport;
            outputScroll.content = outputContent;
            outputScroll.horizontal = false;
            outputScroll.movementType = ScrollRect.MovementType.Clamped;
            outputScroll.scrollSensitivity = 30f;

            // V2: the Brain's screen (480 x 240: drawing, text, touch) and the controller's (a tab over the output; the
            // bottom grows while it's shown so the screen is near its real size).
            screenBox = Ui.Image(bottom, "Brain Screen", new Color(0.02f, 0.02f, 0.03f, 1f), 6f).rectTransform;
            screenBox.Fill(0, 30, 0, 0);
            BrainScreenView.Create(screenBox);
            graphBox = Ui.Image(bottom, "Graph Box", new Color(0.05f, 0.055f, 0.065f, 1f), 6f).rectTransform;
            graphBox.Fill(0, 30, 0, 0);
            TuningGraphView.Create(graphBox);
            monitorBox = Ui.Image(bottom, "Monitor Box", new Color(0.05f, 0.055f, 0.065f, 1f), 6f, true).rectTransform;
            monitorBox.Fill(0, 30, 0, 0);
            var monViewport = Ui.Rect("Viewport", monitorBox).Fill(8, 6, 8, 6);
            monViewport.gameObject.AddComponent<RectMask2D>();
            var monContent = Ui.Rect("Content", monViewport);
            monContent.anchorMin = new Vector2(0f, 1f);
            monContent.anchorMax = new Vector2(1f, 1f);
            monContent.pivot = new Vector2(0.5f, 1f);
            monContent.sizeDelta = Vector2.zero;
            Ui.Column(monContent, 0f);
            monContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            monitorText = Ui.Text(monContent, "Text", "", 13f, Theme.Text, MonoFont.Get);
            monitorText.textWrappingMode = TextWrappingModes.Normal;
            monitorText.overflowMode = TextOverflowModes.Overflow;
            monitorText.alignment = TextAlignmentOptions.TopLeft;
            monitorText.richText = true;
            monitorText.raycastTarget = true;
            monitorText.gameObject.AddComponent<LinkClicks>().owner = this;
            var monScroll = monitorBox.gameObject.AddComponent<ScrollRect>();
            monScroll.viewport = monViewport;
            monScroll.content = monContent;
            monScroll.horizontal = false;
            monScroll.movementType = ScrollRect.MovementType.Clamped;
            monScroll.scrollSensitivity = 30f;
            bottomArea = bottom;

            // The editor (the rest).
            var editor = Ui.Image(body, "Editor", new Color(0.075f, 0.08f, 0.095f, 1f), 6f, true).rectTransform;
            editor.Fill(12, 88, 12, 12 + outputHeight + 8f);
            editorArea = editor;
            var gutterBox = Ui.Rect("Gutter", editor);
            gutterBox.anchorMin = Vector2.zero;
            gutterBox.anchorMax = new Vector2(0f, 1f);
            gutterBox.pivot = new Vector2(0f, 0.5f);
            gutterBox.offsetMin = new Vector2(0f, 6f);
            gutterBox.offsetMax = new Vector2(46f, -6f);
            gutterBox.gameObject.AddComponent<RectMask2D>();
            gutter = Ui.Text(gutterBox, "Numbers", "1", FontSize, Theme.TextFaint, MonoFont.Get, TextAlignmentOptions.TopRight);
            gutter.rectTransform.Fill(0, 0, 8, 0);
            gutter.overflowMode = TextOverflowModes.Overflow;
            gutter.richText = true;

            viewport = Ui.Image(editor, "Viewport", new Color(0f, 0f, 0f, 0f), 0f, true).rectTransform;
            viewport.Fill(50, 6, 6, 6);
            viewport.gameObject.AddComponent<RectMask2D>();
            errorBar = Ui.Image(viewport, "Error Line", new Color(Theme.Danger.r, Theme.Danger.g, Theme.Danger.b, 0.22f)).rectTransform;
            errorBar.anchorMin = new Vector2(0f, 1f);
            errorBar.anchorMax = new Vector2(1f, 1f);
            errorBar.pivot = new Vector2(0.5f, 1f);
            errorBar.gameObject.SetActive(false);
            for (var i = 0; i < 6; i++) {
                var bar2 = Ui.Image(viewport, "Running Line", new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.1f)).rectTransform;
                bar2.anchorMin = new Vector2(0f, 1f);
                bar2.anchorMax = new Vector2(1f, 1f);
                bar2.pivot = new Vector2(0.5f, 1f);
                bar2.gameObject.SetActive(false);
                runBars.Add(bar2);
            }
            highlight = Ui.Text(viewport, "Highlight", "", FontSize, new Color(0.83f, 0.83f, 0.83f), MonoFont.Get, TextAlignmentOptions.TopLeft);
            Setup(highlight);
            highlight.richText = true;
            var text = Ui.Text(viewport, "Text", "", FontSize, new Color(1f, 1f, 1f, 0f), MonoFont.Get, TextAlignmentOptions.TopLeft);
            Setup(text);
            text.richText = false;

            field = viewport.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = viewport;
            field.textComponent = text;
            field.fontAsset = MonoFont.Get;
            field.pointSize = FontSize;
            field.lineType = TMP_InputField.LineType.MultiLineNewline;
            field.richText = false;
            field.caretColor = new Color(0.9f, 0.9f, 0.9f);
            field.customCaretColor = true;
            field.caretWidth = 2;
            field.selectionColor = new Color(0.17f, 0.36f, 0.6f, 0.6f);
            field.scrollSensitivity = 3f;
            field.resetOnDeActivation = false;
            field.restoreOriginalTextOnEscape = false;
            field.onFocusSelectAll = false;
            var navigation = field.navigation;
            navigation.mode = Navigation.Mode.None;
            field.navigation = navigation;
            field.SetTextWithoutNotify(CodeStore.Source);
            field.onValidateInput = Validate;
            field.onValueChanged.AddListener(Changed);
            field.onEndEdit.AddListener(_ => {
                InputEvents.TextInput.EditEndedFrame = Time.frameCount;
                Commit();
            });

            // Resize from the bottom-right corner.
            var grip = Ui.Image(window, "Grip", new Color(1f, 1f, 1f, 0.12f), 3f, true).rectTransform;
            grip.Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-3f, 3f), new Vector2(14f, 14f));
            grip.gameObject.AddComponent<Resizer>().owner = this;
            Ui.Tip(grip, "Resize");

            SetTab(Mathf.Clamp(PlayerPrefs.GetInt(TabKey, 0), 0, 3));
            if (RobotProgram.Current != null) OnState();
            UpdateButtons();
            KeepOnScreen();
            if (string.IsNullOrEmpty(CodeStore.Source)) {
                output.Append("<color=#8A93A0>No program yet: write one here, pick one from Examples, or Open in VS Code.\n"
                              + "Set the ports to the ones in Edit > Devices. Run starts the simulation.</color>\n");
                outputDirty = true;
            }
        }

        private static void Setup(TextMeshProUGUI t) {
            t.rectTransform.Fill();
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
        }

        private void SetTab(bool screen) => SetTab(screen ? 1 : 0);

        // V2: 0 output, 1 the Brain's (and controller's) screen, 2 the tuning graph, 3 the Monitor.
        private void SetTab(int tab) {
            this.tab = tab;
            showScreen = tab == 1;
            monitorBox.gameObject.SetActive(tab == 3);
            monitorTab.targetGraphic.color = tab == 3 ? Theme.Hover : Theme.PanelRaised;
            monitorNext = 0f;
            PlayerPrefs.SetInt(TabKey, tab);
            screenBox.gameObject.SetActive(tab == 1);
            graphBox.gameObject.SetActive(tab == 2);
            // The Brain's screen and the graph get room (the editor gives some up while they're shown).
            var height = tab == 0 ? OutputHeight : ScreenTabHeight;
            if (bottomArea != null) bottomArea.offsetMax = new Vector2(-12f, 12f + height);
            if (editorArea != null) editorArea.offsetMin = new Vector2(12f, 12f + height + 8f);
            outputScroll.gameObject.SetActive(tab == 0);
            outputTab.targetGraphic.color = tab == 0 ? Theme.Hover : Theme.PanelRaised;
            screenTab.targetGraphic.color = tab == 1 ? Theme.Hover : Theme.PanelRaised;
            graphTab.targetGraphic.color = tab == 2 ? Theme.Hover : Theme.PanelRaised;
            screenDirty = true;
        }

        // ------------------------------------------------------------------ editing

        private char Validate(string text, int index, char added) {
            // A paste comes in as many characters at once: it keeps its own indentation (no auto-indent), and its
            // tabs become 4 spaces afterwards.
            if (validateFrame != Time.frameCount) {
                validateFrame = Time.frameCount;
                validateCount = 0;
            }
            validateCount++;
            if (validateCount > 1 || Pasting()) {
                pendingInsert = null;
                if (added == '\t') pastedTabs = true;
                return added;
            }
            if (added == '\t') {
                pendingInsert = "    ";
                return '\0';
            }
            if (added == '}' && Scripting.CppTranslator.LooksLikeCpp(text)) {
                // V2 (C++): a } typed where the line is only spaces goes back a level.
                var ls = index == 0 ? 0 : text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
                if (index - ls >= 4 && text.Substring(ls, index - ls).Trim().Length == 0) pendingCloseBrace = true;
                return added;
            }
            if (added == '\n') {
                // Keep the line's indentation; one more level after a ':'.
                var lineStart = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
                if (index == 0) lineStart = 0;
                var line = text.Substring(lineStart, Math.Max(0, index - lineStart));
                var indent = line.Length - line.TrimStart(' ').Length;
                var cpp = Scripting.CppTranslator.LooksLikeCpp(text);
                var code = cpp ? StripCppComment(line).TrimEnd() : StripComment(line).TrimEnd();
                if (cpp) {
                    if (code.EndsWith("{")) indent += 4;   // V2 (C++)
                }
                else if (code.EndsWith(":")) indent += 4;
                else if (Regex(code)) indent = Math.Max(0, indent - 4);
                if (indent > 0) pendingInsert = new string(' ', indent);
                return '\n';
            }
            return added;
        }

        private static string StripCppComment(string line) {
            var inString = '\0';
            for (var i = 0; i < line.Length; i++) {
                var c = line[i];
                if (inString != '\0') {
                    if (c == '\\') { i++; continue; }
                    if (c == inString) inString = '\0';
                    continue;
                }
                if (c == '"' || c == '\'') inString = c;
                else if (c == '/' && i + 1 < line.Length && line[i + 1] == '/') return line.Substring(0, i);
            }
            return line;
        }

        private static bool Pasting() {
            var kb = Keyboard.current;
            if (kb == null) return false;
            var ctrl = kb.ctrlKey.isPressed || kb.leftCommandKey.isPressed || kb.rightCommandKey.isPressed;
            return (ctrl && kb.vKey.isPressed) || (kb.shiftKey.isPressed && kb.insertKey.isPressed);
        }

        // A line that ends its block (return, break, continue, pass, raise): the next line dedents.
        private static bool Regex(string code) {
            var t = code.Trim();
            return t == "pass" || t == "break" || t == "continue" || t == "return" || t.StartsWith("return ") || t.StartsWith("raise ");
        }

        private static string StripComment(string line) {
            var inString = '\0';
            for (var i = 0; i < line.Length; i++) {
                var c = line[i];
                if (inString != '\0') {
                    if (c == inString) inString = '\0';
                    continue;
                }
                if (c == '\'' || c == '"') inString = c;
                else if (c == '#') return line.Substring(0, i);
            }
            return line;
        }

        private void Changed(string value) {
            if (syncing) return;
            highlightDirty = true;
            if (errorLine > 0) {
                errorLine = 0;
                highlightDirty = true;
            }
            if (Time.unscaledTime - lastSnapshot > 0.7f || value.EndsWith("\n")) Snapshot();
        }

        private void Snapshot() {
            var text = field.text;
            if (undo.Count > 0 && undo[undo.Count - 1].text == text) return;
            undo.Add((text, field.stringPosition));
            if (undo.Count > 200) undo.RemoveAt(0);
            redo.Clear();
            lastSnapshot = Time.unscaledTime;
        }

        private void SetText(string text, int caret) {
            syncing = true;
            field.text = text;
            syncing = false;
            field.stringPosition = field.selectionStringAnchorPosition = field.selectionStringFocusPosition = Mathf.Clamp(caret, 0, text.Length);
            highlightDirty = true;
        }

        /// <summary>The text box into the store (the build saves it).</summary>
        private void Commit() {
            if (field != null) CodeStore.Set(field.text);
        }

        private void OnStoreChanged() {
            if (field == null || field.text == CodeStore.Source) return;
            Snapshot();
            SetText(CodeStore.Source, field.stringPosition);
            Snapshot();
        }

        private void InsertDevices() {
            string text;
            try { text = DeviceConfig.InsertInto(field.text); }
            catch (System.Exception e) {
                Debug.LogException(e);
                Interface.Interface.Say("The devices couldn't be read: " + e.Message, false);
                return;
            }
            Snapshot();
            SetText(text, 0);
            Snapshot();
            Commit();
            output.Append("<color=#8A93A0>Robot configuration inserted from the build (Edit > Devices has the ports; names come from the names you gave the motors).</color>\n");
            outputDirty = true;
        }

        private void UseExample(int i) {
            Snapshot();
            SetText(Examples.Fill(Examples.All[i].code), 0);   // V2: with the robot's own devices
            Snapshot();
            Commit();
            output.Append("<color=#8A93A0>Example: " + Escape(Examples.All[i].description) + ". Check its ports against Edit > Devices.</color>\n");
            outputDirty = true;
        }

        private void HandleKeys() {
            var kb = Keyboard.current;
            if (kb == null) return;
            var ctrl = kb.ctrlKey.isPressed || kb.leftCommandKey.isPressed;
            if (kb.f5Key.wasPressedThisFrame) {
                if (kb.shiftKey.isPressed) RobotProgram.Stop("Stopped");
                else Run();
                return;
            }
            if (kb.f6Key.wasPressedThisFrame) {   // V2: pause / go on
                TogglePause();
                return;
            }
            if (kb.f10Key.wasPressedThisFrame) {   // V2: a step while paused (Shift: 0.1 s)
                StepTime(kb.shiftKey.isPressed ? 24 : 1);
                return;
            }
            if (!field.isFocused) return;
            if (pastedTabs) {
                pastedTabs = false;
                var caret = Mathf.Clamp(field.stringPosition, 0, field.text.Length);
                var before = field.text.Substring(0, caret);
                var tabsBefore = before.Length - before.Replace("\t", "").Length;
                SetText(field.text.Replace("\t", "    "), caret + tabsBefore * 3);
                Changed(field.text);
            }
            if (pendingCloseBrace) {
                // The } went in after the spaces: take 4 of them out.
                pendingCloseBrace = false;
                var at = Mathf.Clamp(field.stringPosition, 0, field.text.Length);
                var brace = at - 1;
                if (brace >= 4 && field.text[brace] == '}' && field.text.Substring(brace - 4, 4) == "    ") {
                    SetText(field.text.Remove(brace - 4, 4), at - 4);
                    Changed(field.text);
                }
            }
            if (pendingInsert != null) {
                var insert = pendingInsert;
                pendingInsert = null;
                var at = Mathf.Clamp(field.stringPosition, 0, field.text.Length);
                SetText(field.text.Insert(at, insert), at + insert.Length);
                Changed(field.text);
            }
            if (ctrl && kb.zKey.wasPressedThisFrame && !kb.shiftKey.isPressed) {
                Snapshot();
                if (undo.Count > 1) {
                    redo.Add(undo[undo.Count - 1]);
                    undo.RemoveAt(undo.Count - 1);
                    var (t, c) = undo[undo.Count - 1];
                    SetText(t, c);
                }
            }
            else if (ctrl && (kb.yKey.wasPressedThisFrame || kb.zKey.wasPressedThisFrame && kb.shiftKey.isPressed)) {
                if (redo.Count > 0) {
                    var r = redo[redo.Count - 1];
                    redo.RemoveAt(redo.Count - 1);
                    undo.Add(r);
                    SetText(r.text, r.caret);
                }
            }
            else if (ctrl && kb.slashKey.wasPressedThisFrame) ToggleComment();
            else if (kb.tabKey.wasPressedThisFrame && kb.shiftKey.isPressed) Dedent();
        }

        // The lines under the caret or selection.
        private (int start, int end) SelectedLines() {
            var text = field.text;
            var a = Mathf.Min(field.selectionStringAnchorPosition, field.selectionStringFocusPosition);
            var b = Mathf.Max(field.selectionStringAnchorPosition, field.selectionStringFocusPosition);
            a = Mathf.Clamp(a, 0, text.Length);
            b = Mathf.Clamp(b, 0, text.Length);
            var start = a == 0 ? 0 : text.LastIndexOf('\n', a - 1) + 1;
            var end = text.IndexOf('\n', b);
            if (end < 0) end = text.Length;
            return (start, end);
        }

        private void ToggleComment() {
            Snapshot();
            var (start, end) = SelectedLines();
            var text = field.text;
            var lines = text.Substring(start, end - start).Split('\n');
            var mark = Scripting.CppTranslator.LooksLikeCpp(text) ? "//" : "#";   // V2: C++ comments
            var all = lines.Where(l => l.Trim().Length > 0).All(l => l.TrimStart().StartsWith(mark));
            for (var i = 0; i < lines.Length; i++) {
                if (lines[i].Trim().Length == 0) continue;
                var indent = lines[i].Length - lines[i].TrimStart().Length;
                if (all) {
                    var rest = lines[i].Substring(indent + mark.Length);
                    lines[i] = lines[i].Substring(0, indent) + (rest.StartsWith(" ") ? rest.Substring(1) : rest);
                }
                else lines[i] = lines[i].Substring(0, indent) + mark + " " + lines[i].Substring(indent);
            }
            var joined = string.Join("\n", lines);
            SetText(text.Substring(0, start) + joined + text.Substring(end), start + joined.Length);
            Snapshot();
        }

        private void Dedent() {
            Snapshot();
            var (start, end) = SelectedLines();
            var text = field.text;
            var lines = text.Substring(start, end - start).Split('\n');
            for (var i = 0; i < lines.Length; i++) {
                var n = 0;
                while (n < 4 && n < lines[i].Length && lines[i][n] == ' ') n++;
                lines[i] = lines[i].Substring(n);
            }
            var joined = string.Join("\n", lines);
            SetText(text.Substring(0, start) + joined + text.Substring(end), start);
            Snapshot();
        }

        // ------------------------------------------------------------------ running

        private double Limit => period switch {
            1 => 15,
            2 => 60,
            3 => 105,
            _ => 0,
        };

        private RobotProgram.Period ProgramPeriod => period switch {
            1 or 2 => RobotProgram.Period.Autonomous,
            3 => RobotProgram.Period.Driver,
            _ => RobotProgram.Period.Program,
        };

        private void Run() {
            Commit();
            if (string.IsNullOrWhiteSpace(CodeStore.Source)) {
                Interface.Interface.Say("There's no program to run: write one, or pick one from Examples", false);
                return;
            }
            // A mistake in how it's written shows before anything starts.
            try {
                if (Scripting.CppTranslator.LooksLikeCpp(CodeStore.Source)) Scripting.Compiler.CompileModule(Scripting.CppTranslator.Translate(CodeStore.Source, out _));   // V2: C++
                else Scripting.Compiler.Compile(CodeStore.Source);
            }
            catch (Scripting.SyntaxErr e) {
                ShowError(e.line, "Line " + e.line + ": SyntaxError: " + e.Message);
                return;
            }
            if (!Sandbox.Running) {
                if (Game.SkillsGame.Active) {
                    Interface.Interface.Say("Start the run from the game's own buttons", false);
                    return;
                }
                Sandbox.Begin();
                if (!Sandbox.Running) return;
            }
            if (Sandbox.Instance != null && Sandbox.Instance.Paused) Sandbox.Instance.SetPaused(false);   // V2: Run goes on from a pause
            output.Clear();
            consoleColor = null;
            errorLine = 0;
            highlightDirty = true;
            if (!RobotProgram.Start(CodeStore.Source, ProgramPeriod, Limit, out var error, out var line)) {
                ShowError(line, error);
                return;
            }
            output.Append("<color=#6FCF97>Running: " + Escape(PeriodLabels[period]) + "</color>\n");
            outputDirty = true;
            // (The tab stays: the graph or the Brain's screen is often what's being watched.)
            UpdateButtons();
        }

        private void ShowError(int line, string message) {
            errorLine = line;
            highlightDirty = true;
            output.Append("<color=#EB5757>" + Escape(message) + "</color>\n");
            outputDirty = true;
            SetTab(false);
            Audio.Sounds.Play(Audio.Sounds.Error);
            ScrollToLine(line);
        }

        private void OnPrinted(string text) {
            if (text.StartsWith("\nError on line")) output.Append("<color=#EB5757>" + Escape(text) + "</color>");
            else if (text.StartsWith("[warning")) output.Append("<color=#F2C94C>" + Escape(text) + "</color>");
            else if (text.StartsWith("[")) output.Append("<color=#8A93A0>" + Escape(text) + "</color>");
            else Console(text);
            if (output.Length > 40000) output.Remove(0, output.Length - 30000);
            outputDirty = true;
        }

        private void OnState() {
            var p = RobotProgram.Current;
            if (p != null && p.Failed) {
                errorLine = p.ErrorLine;
                highlightDirty = true;
                Audio.Sounds.Play(Audio.Sounds.Error);
                ScrollToLine(errorLine);
            }
            UpdateButtons();
        }

        private void OnScreen() => screenDirty = true;

        private void UpdateButtons() {
            if (stopButton == null) return;
            stopButton.interactable = RobotProgram.Active;
        }

        // ------------------------------------------------------------------ every frame

        private void Update() {
            CodeStore.Poll();
            HandleKeys();
            var p = RobotProgram.Current;
            status.text = p == null ? (Sandbox.Running ? "Not running: the keys drive the robot" : "Not running")
                : p.Failed ? "<color=#EB5757>" + p.Status + "</color>"
                : p.Status == "Running" ? "Running · " + p.Time.ToString("0.0", CultureInfo.InvariantCulture) + " s"
                : p.Status;
            stopButton.interactable = RobotProgram.Active;
            // V2: Pause / Step, and the Monitor (10 times a second; at once after a click, a pause or a step).
            var paused = Sandbox.Running && Sandbox.Instance != null && Sandbox.Instance.Paused;
            if (paused) status.text = "<color=#F2C94C>Paused</color> · " + status.text;
            pauseButton.interactable = Sandbox.Running;
            var pauseLabel = pauseButton.GetComponentInChildren<TextMeshProUGUI>();
            if (pauseLabel != null) pauseLabel.text = paused ? "Go on" : "Pause";
            if (stepButton.gameObject.activeSelf != paused) stepButton.gameObject.SetActive(paused);
            if (tab == 3 && Time.unscaledTime >= monitorNext) {
                monitorNext = Time.unscaledTime + 0.1f;
                monitorText.text = monitor.Build(p);
            }
            var lines = 1 + field.text.Count(c => c == '\n');
            subtitle.text = (Scripting.CppTranslator.LooksLikeCpp(field.text) ? "C++ · " : "Python · ") + lines + (lines == 1 ? " line" : " lines") + (CodeStore.Watching ? " · synced with VS Code" : "");
            if (highlightDirty) {
                highlightDirty = false;
                highlight.text = PythonColors.Highlight(field.text);
                var sb = new StringBuilder();
                for (var i = 1; i <= lines; i++) {
                    if (i == errorLine) sb.Append("<color=#EB5757>").Append(i).Append("</color>");
                    else sb.Append(i);
                    sb.Append('\n');
                }
                gutter.text = sb.ToString();
            }
            if (outputDirty) {
                outputDirty = false;
                var atBottom = outputScroll.verticalNormalizedPosition < 0.02f || outputContent.rect.height <= ((RectTransform)outputScroll.viewport).rect.height + 4f;
                outputText.text = output.ToString();
                if (atBottom) {
                    Canvas.ForceUpdateCanvases();
                    outputScroll.verticalNormalizedPosition = 0f;
                }
            }
            screenDirty = false;   // (the Brain screen view redraws itself)
        }

        private void LateUpdate() {
            // The colored copy and the line numbers follow the text box's scrolling.
            var t = field.textComponent.rectTransform;
            highlight.rectTransform.anchoredPosition = t.anchoredPosition;
            gutter.rectTransform.anchoredPosition = new Vector2(gutter.rectTransform.anchoredPosition.x, t.anchoredPosition.y);
            if (errorLine > 0 && LineBounds(errorLine, out var top, out var bottom)) {
                errorBar.gameObject.SetActive(true);
                errorBar.anchoredPosition = new Vector2(0f, t.anchoredPosition.y + top);
                errorBar.sizeDelta = new Vector2(0f, top - bottom);
            }
            else errorBar.gameObject.SetActive(false);
            // V2: the lines the program's threads are on (the one opened in the Monitor brighter).
            var p = RobotProgram.Current;
            var count = 0;
            if (p != null && RobotProgram.Active) {
                var threads = p.Threads.ToList();
                var selected = Mathf.Clamp(monitor.selectedThread, 0, Mathf.Max(0, threads.Count - 1));
                for (var i = 0; i < threads.Count && count < runBars.Count; i++) {
                    var line = threads[i].Line;
                    if (line <= 0 || line == errorLine || !LineBounds(line, out var top2, out var bottom2)) continue;
                    var bar = runBars[count++];
                    bar.gameObject.SetActive(true);
                    bar.anchoredPosition = new Vector2(0f, t.anchoredPosition.y + top2);
                    bar.sizeDelta = new Vector2(0f, top2 - bottom2);
                    bar.GetComponent<Image>().color = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, i == selected ? 0.22f : 0.09f);
                }
            }
            for (var i = count; i < runBars.Count; i++)
                if (runBars[i].gameObject.activeSelf) runBars[i].gameObject.SetActive(false);
        }

        // A line's top and bottom in the text's own space (y down from its top edge, negative).
        private bool LineBounds(int line, out float top, out float bottom) {
            var lineHeight = field.textComponent.font.faceInfo.lineHeight * FontSize / Mathf.Max(1f, field.textComponent.font.faceInfo.pointSize);
            top = -(line - 1) * lineHeight;
            bottom = top - lineHeight;
            return line >= 1;
        }

        private void ScrollToLine(int line) {
            if (line <= 0 || field == null) return;
            var text = field.text;
            var index = 0;
            for (var i = 1; i < line && index >= 0; i++) index = text.IndexOf('\n', index) + 1;
            if (index < 0) return;
            EventSystem.current?.SetSelectedGameObject(field.gameObject);
            field.ActivateInputField();
            field.stringPosition = field.selectionStringAnchorPosition = field.selectionStringFocusPosition = Mathf.Clamp(index, 0, text.Length);
        }

        private void ShowHelp() {
            var d = Dialog.Open("Robot code", new Vector2(640f, 560f));
            var scrollRect = Ui.Rect("Scroll", d.Body).Fill();
            scrollRect.gameObject.AddComponent<RectMask2D>();
            var content = Ui.Rect("Content", scrollRect);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            Ui.Column(content, 0f);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var text = Ui.Text(content, "Text", HelpText, Theme.FontBody, Theme.Text);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.richText = true;
            var s = scrollRect.gameObject.AddComponent<ScrollRect>();
            s.viewport = scrollRect;
            s.content = content;
            s.horizontal = false;
            s.movementType = ScrollRect.MovementType.Clamped;
            s.scrollSensitivity = 30f;
            Ui.Button(d.Footer, "Done", "Done", d.Close, true).Size(90f, 34f);
        }

        private const string HelpText =
            "Robot code is <b>Python</b>, written like <b>VEXcode V5 Python</b>: start with <noparse>from vex import *</noparse>, make the devices on their ports " +
            "(Edit > Devices shows which device is on which port), and drive them. It runs on the simulated robot: <b>Run</b> starts the simulation if it isn't going.\n\n" +
            "<b>Devices</b>\n" +
            "<color=#9CDCFE>Motor(Ports.PORT1, GearSetting.RATIO_18_1, False)</color>: spin(FORWARD, 50, PERCENT), spin_for(FORWARD, 360, DEGREES), spin_to_position(90, DEGREES), stop(BRAKE), position(DEGREES), velocity(RPM), set_velocity, set_stopping(HOLD), reset_position\n" +
            "<color=#9CDCFE>MotorGroup(m1, m2)</color>, <color=#9CDCFE>DriveTrain(left, right, wheel_travel, track_width, wheel_base, INCHES, ratio)</color>: drive_for(FORWARD, 24, INCHES), turn_for(RIGHT, 90, DEGREES), drive, turn, stop, arcade\n" +
            "<color=#9CDCFE>SmartDrive(left, right, inertial, ...)</color>: turns by the gyro, turn_to_heading(90, DEGREES)\n" +
            "<color=#9CDCFE>Inertial(port)</color>: calibrate(), heading(), rotation()  ·  <color=#9CDCFE>Distance(port)</color>: object_distance(INCHES)  ·  <color=#9CDCFE>Optical(port)</color>: hue(), brightness(), color()  ·  <color=#9CDCFE>Rotation(port)</color>: angle(), position()\n" +
            "<color=#9CDCFE>Bumper / Limit(brain.three_wire_port.a)</color>: pressing(), pressed(callback)  ·  <color=#9CDCFE>Pneumatics(brain.three_wire_port.a)</color>: open(), close()\n" +
            "<color=#9CDCFE>Controller()</color>: axis1-4.position() (-100 to 100), buttonA.pressing(), buttonA.pressed(callback); its keys are in Edit > Controller\n" +
            "<color=#9CDCFE>Brain()</color>: screen.print(...), screen.next_row(), timer.time(SECONDS)\n\n" +
            "<b>Time and threads</b>\n" +
            "wait(20, MSEC) pauses the program (simulated time: the physics runs between). A loop that never waits still works, but runs the robot's brain flat out. " +
            "Thread(function) runs a function at the same time; Competition(driver_control, autonomous) runs one of them by what you pick beside Run.\n\n" +
            "<b>Python</b>\n" +
            "Functions, classes, lists, dicts, f-strings, try/except, lambda, comprehensions, global/nonlocal, and the math, random and time modules. Not supported: generators (yield), 'with', async.\n\n" +
            "<b>Finding mistakes</b>\n" +
            "print() writes to Output (set_console_text_color(Color.RED) or \\033[31m color it, clear_console() empties it; \"name: value\" prints go on the Graph). " +
            "The <b>Monitor</b> tab shows each thread and the line it's on (marked in the editor), its calls and their local variables, and the program's variables; " +
            "monitor_variable(\"speed\") and monitor_sensor(\"left_motor.position\") put those at the top. <b>Pause</b> (F6) stops time to look, <b>Step</b> (F10) moves it 1/240 s (Shift: 0.1 s).\n\n" +
            "<b>Keys</b>\n" +
            "F5 runs, Shift+F5 stops, F6 pauses, F10 steps. Tab indents (4 spaces), Shift+Tab un-indents, Ctrl+/ comments lines, Ctrl+Z / Ctrl+Y undo and redo. " +
            "The program is saved in the build. Open in VS Code edits it there (completions included); every save comes back here.";

        private static string Escape(string s) => "<noparse>" + s.Replace("</noparse>", "") + "</noparse>";

        // ------------------------------------------------------------------ V2: debugging

        private void TogglePause() {
            var sandbox = Sandbox.Instance;
            if (!Sandbox.Running || sandbox == null) {
                Interface.Interface.Say("Nothing to pause: run the program first", false);
                return;
            }
            if (Game.SkillsGame.Active && !sandbox.Paused) {
                Interface.Interface.Say("A game's run can't be paused (its clock and score keep going)", false);
                return;
            }
            sandbox.SetPaused(!sandbox.Paused);
            monitorNext = 0f;
            UpdateButtons();
        }

        private void StepTime(int steps) {
            var sandbox = Sandbox.Instance;
            if (sandbox == null || !sandbox.Paused) {
                Interface.Interface.Say("Step works while paused (Pause, F6)", false);
                return;
            }
            for (var i = 0; i < steps; i++) sandbox.StepOnce();
            monitorNext = 0f;
        }

        // The console: VEXcode's escape codes (\033[31m colors, \033[0m back to normal, \033[2J clears).
        private void Console(string text) {
            var start = 0;
            while (start < text.Length) {
                var esc = text.IndexOf('\u001b', start);
                var piece = esc < 0 ? text.Substring(start) : text.Substring(start, esc - start);
                if (piece.Length > 0) {
                    if (consoleColor != null) output.Append("<color=").Append(consoleColor).Append('>').Append(Escape(piece)).Append("</color>");
                    else output.Append(Escape(piece));
                }
                if (esc < 0) break;
                // ESC [ numbers ; ... letter
                var end = esc + 1;
                if (end < text.Length && text[end] == '[') end++;
                var codeStart = end;
                while (end < text.Length && (char.IsDigit(text[end]) || text[end] == ';')) end++;
                if (end >= text.Length) break;
                var codes = text.Substring(codeStart, end - codeStart);
                var kind = text[end];
                start = end + 1;
                if (kind == 'J' && (codes == "2" || codes == "3")) output.Clear();
                else if (kind == 'm') consoleColor = AnsiColor(codes, consoleColor);
            }
        }

        private static string AnsiColor(string codes, string current) {
            var n = codes.Split(';').Select(c => int.TryParse(c, out var v) ? v : 0).ToArray();
            if (n.Length == 0 || n[0] == 0 || n[0] == 39) return null;
            Color32 c;
            if (n[0] == 38 && n.Length >= 5 && n[1] == 2) c = new Color32((byte)n[2], (byte)n[3], (byte)n[4], 255);
            else if (n[0] >= 30 && n[0] <= 37 || n[0] >= 90 && n[0] <= 97) {
                var i = n[0] % 10;
                c = i switch {
                    0 => new Color32(0, 0, 0, 255),
                    1 => new Color32(255, 0, 0, 255),
                    2 => new Color32(0, 255, 0, 255),
                    3 => new Color32(255, 255, 0, 255),
                    4 => new Color32(0, 0, 255, 255),
                    5 => new Color32(255, 0, 255, 255),
                    6 => new Color32(0, 255, 255, 255),
                    _ => new Color32(255, 255, 255, 255),
                };
            }
            else return current;
            // On the dark console: pure blue or black would be unreadable, so dark colors are lightened.
            var col = (Color)c;
            var luma = 0.3f * col.r + 0.59f * col.g + 0.11f * col.b;
            if (luma < 0.1f) col = new Color(0.55f, 0.58f, 0.63f);
            else if (luma < 0.55f) col = Color.Lerp(col, Color.white, 0.45f);
            return "#" + ColorUtility.ToHtmlStringRGB(col);
        }

        internal void ClickedLink(string id) {
            if (id.StartsWith("t:") && int.TryParse(id.Substring(2), out var thread)) monitor.selectedThread = thread;
            else if (id.StartsWith("l:") && int.TryParse(id.Substring(2), out var line)) ScrollToLine(line);
            else if (id.StartsWith("v:")) {
                var name = id.Substring(2);
                if (!monitor.pinned.Remove(name)) monitor.pinned.Add(name);
            }
            monitorNext = 0f;
        }

        // The Monitor's links (threads, lines, variable names).
        private class LinkClicks : MonoBehaviour, IPointerClickHandler {
            public CodePanel owner;

            public void OnPointerClick(PointerEventData e) {
                var text = GetComponent<TextMeshProUGUI>();
                var link = TMP_TextUtilities.FindIntersectingLink(text, e.position, null);
                if (link < 0) return;
                owner.ClickedLink(text.textInfo.linkInfo[link].GetLinkID());
            }
        }

        // ------------------------------------------------------------------ moving and resizing

        private void KeepOnScreen() {
            if (window == null || Interface.Interface.Root == null) return;
            var root = ((RectTransform)Interface.Interface.Root).rect;
            var p = window.anchoredPosition;
            p.x = Mathf.Clamp(p.x, -window.sizeDelta.x * 0.7f, root.width - 80f);
            p.y = Mathf.Clamp(p.y, -root.height + 40f, 0f);
            window.anchoredPosition = p;
        }

        private void SaveRect() {
            var p = window.anchoredPosition;
            var s = window.sizeDelta;
            PlayerPrefs.SetString(RectKey, string.Join(",", new[] { p.x, p.y, s.x, s.y }.Select(v => v.ToString(CultureInfo.InvariantCulture))));
        }

        private class Mover : MonoBehaviour, IDragHandler, IEndDragHandler, IPointerDownHandler {
            public CodePanel owner;
            public void OnPointerDown(PointerEventData e) => owner.transform.SetAsLastSibling();

            public void OnDrag(PointerEventData e) {
                owner.window.anchoredPosition += e.delta / Mathf.Max(Interface.Interface.Root.lossyScale.x, 0.0001f);
                owner.KeepOnScreen();
            }

            public void OnEndDrag(PointerEventData e) => owner.SaveRect();
        }

        private class Resizer : MonoBehaviour, IDragHandler, IEndDragHandler {
            public CodePanel owner;

            public void OnDrag(PointerEventData e) {
                var d = e.delta / Mathf.Max(Interface.Interface.Root.lossyScale.x, 0.0001f);
                var s = owner.window.sizeDelta;
                owner.window.sizeDelta = new Vector2(Mathf.Max(460f, s.x + d.x), Mathf.Max(360f, s.y - d.y));
            }

            public void OnEndDrag(PointerEventData e) => owner.SaveRect();
        }
    }

    /// <summary>A monospaced font for code (the system's Consolas / Cascadia Mono / Menlo / Courier New, made into a
    /// TextMeshPro font once), with the interface font behind it for anything it lacks.</summary>
    public static class MonoFont {
        private static TMP_FontAsset font;
        private static bool tried;

        public static TMP_FontAsset Get {
            get {
                if (font != null || tried) return font != null ? font : Theme.Regular;
                tried = true;
                foreach (var family in new[] { "Consolas", "Cascadia Mono", "Menlo", "Courier New", "DejaVu Sans Mono", "Liberation Mono" }) {
                    try {
                        font = TMP_FontAsset.CreateFontAsset(family, "Regular", 90);
                    }
                    catch (Exception e) {
                        Debug.Log("[Robot code] font " + family + ": " + e.Message);
                    }
                    if (font != null) break;
                }
                if (font == null) return Theme.Regular;
                font.name = "Code (" + font.faceInfo.familyName + ")";
                if (Theme.Regular != null) font.fallbackFontAssetTable = new List<TMP_FontAsset> { Theme.Regular };
                return font;
            }
        }
    }

    /// <summary>Python's colors as VS Code's dark theme shows them, as TextMeshPro rich text (the characters
    /// themselves unchanged, so it lines up with the text box).</summary>
    public static class PythonColors {
        // V2: C++ (PROS / LemLib / VEXcode C++), the same colors as VS Code's dark theme.
        private static readonly HashSet<string> CppControl = new() { "if", "else", "for", "while", "do", "switch", "case", "default", "break", "continue", "return", "goto", "try", "catch", "throw", "new", "delete", "using", "namespace", "template", "typename", "sizeof" };
        private static readonly HashSet<string> CppStorage = new() { "void", "int", "double", "float", "bool", "char", "long", "short", "unsigned", "signed", "auto", "const", "constexpr", "static", "inline", "extern", "struct", "class", "enum", "public", "private", "protected", "virtual", "override", "true", "false", "nullptr", "NULL", "this", "uint8_t", "uint32_t", "int32_t", "size_t", "std" };

        private static string HighlightCpp(string text) {
            var sb = new StringBuilder(text.Length * 2);
            var i = 0;
            var lineStart = true;
            while (i < text.Length) {
                var c = text[i];
                if (c == '\n') { lineStart = true; Plainly(sb, "\n"); i++; continue; }
                if (lineStart && c == '#') {
                    var end = text.IndexOf('\n', i);
                    if (end < 0) end = text.Length;
                    Span(sb, text, i, end, Keyword);
                    i = end;
                    continue;
                }
                if (c != ' ' && c != '\t') lineStart = false;
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/') {
                    var end = text.IndexOf('\n', i);
                    if (end < 0) end = text.Length;
                    Span(sb, text, i, end, Comment);
                    i = end;
                    continue;
                }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') {
                    var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    end = end < 0 ? text.Length : end + 2;
                    // (a block comment over several lines: colored line by line)
                    var start = i;
                    while (start < end) {
                        var nl = text.IndexOf('\n', start);
                        var stop = nl < 0 || nl > end ? end : nl;
                        Span(sb, text, start, stop, Comment);
                        if (stop < end) Plainly(sb, "\n");
                        start = stop + 1;
                    }
                    i = end;
                    continue;
                }
                if (c == '"' || c == '\'') {
                    var start = i++;
                    while (i < text.Length && text[i] != c && text[i] != '\n') i += text[i] == '\\' ? 2 : 1;
                    if (i < text.Length && text[i] == c) i++;
                    Span(sb, text, start, Math.Min(i, text.Length), Str);
                    continue;
                }
                if (char.IsDigit(c)) {
                    var start = i;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '.' || text[i] == '\'')) i++;
                    Span(sb, text, start, i, Number);
                    continue;
                }
                if (char.IsLetter(c) || c == '_') {
                    var start = i;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                    var word = text.Substring(start, i - start);
                    var next = i;
                    while (next < text.Length && text[next] == ' ') next++;
                    string color;
                    if (CppControl.Contains(word)) color = Keyword;
                    else if (CppStorage.Contains(word)) color = Storage;
                    else if (word is "pros" or "lemlib" or "vex" or "ez") color = Type;
                    else if (next < text.Length && text[next] == ':' && next + 1 < text.Length && text[next + 1] == ':') color = Type;
                    else if (word.Length > 1 && word.All(ch => char.IsUpper(ch) || char.IsDigit(ch) || ch == '_')) color = Constant;
                    else if (next < text.Length && text[next] == '(') color = Function;
                    else if (char.IsUpper(word[0]) && start > 1 && text[start - 1] == ':' && text[start - 2] == ':') color = Type;
                    else color = null;
                    if (color == null) Plainly(sb, word);
                    else Span(sb, text, start, i, color);
                    continue;
                }
                Plainly(sb, c.ToString());
                i++;
            }
            return sb.ToString();
        }

        private const string Keyword = "#C586C0", Storage = "#569CD6", Str = "#CE9178", Comment = "#6A9955", Number = "#B5CEA8",
            Function = "#DCDCAA", Type = "#4EC9B0", Constant = "#4FC1FF", Plain = "#D4D4D4";

        private static readonly HashSet<string> Control = new() { "if", "elif", "else", "for", "while", "break", "continue", "return", "try", "except", "finally", "raise", "import", "from", "as", "pass", "with", "yield", "assert", "del", "in", "is", "not", "and", "or", "global", "nonlocal", "lambda" };
        private static readonly HashSet<string> Declare = new() { "def", "class", "True", "False", "None", "self" };
        private static readonly HashSet<string> Builtin = new() { "print", "range", "len", "abs", "min", "max", "round", "int", "float", "str", "bool", "list", "dict", "tuple", "set", "sum", "sorted", "enumerate", "zip", "map", "filter", "isinstance", "any", "all", "wait", "super", "type" };
        private static readonly HashSet<string> VexTypes = new() { "Brain", "Controller", "Motor", "MotorGroup", "DriveTrain", "SmartDrive", "Inertial", "Distance", "Optical", "Rotation", "Bumper", "Limit", "DigitalIn", "DigitalOut", "Pneumatics", "Encoder", "Potentiometer", "PotentiometerV2", "Line", "Light", "Sonar", "Gyro", "Timer", "Thread", "Competition", "Event", "Ports", "GearSetting", "Color" };

        public static string Highlight(string text) {
            if (Scripting.CppTranslator.LooksLikeCpp(text)) return HighlightCpp(text);   // V2
            var sb = new StringBuilder(text.Length * 2);
            var i = 0;
            var afterDef = false;
            while (i < text.Length) {
                var c = text[i];
                if (c == '#') {
                    var end = text.IndexOf('\n', i);
                    if (end < 0) end = text.Length;
                    Span(sb, text, i, end, Comment);
                    i = end;
                    continue;
                }
                if (c == '"' || c == '\'' || (c is 'f' or 'r' or 'b' or 'F' or 'R' or 'B') && i + 1 < text.Length && (text[i + 1] == '"' || text[i + 1] == '\'')) {
                    var start = i;
                    if (c != '"' && c != '\'') i++;
                    var q = text[i];
                    var triple = i + 2 < text.Length && text[i + 1] == q && text[i + 2] == q;
                    i += triple ? 3 : 1;
                    while (i < text.Length) {
                        if (text[i] == '\\' && i + 1 < text.Length) {
                            i += 2;
                            continue;
                        }
                        if (!triple && text[i] == '\n') break;
                        if (text[i] == q && (!triple || i + 2 < text.Length && text[i + 1] == q && text[i + 2] == q)) {
                            i += triple ? 3 : 1;
                            break;
                        }
                        i++;
                    }
                    Span(sb, text, start, Math.Min(i, text.Length), Str);
                    continue;
                }
                if (char.IsDigit(c) || c == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1]) && (i == 0 || !char.IsLetterOrDigit(text[i - 1]))) {
                    var start = i;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '.' || text[i] == '_')) i++;
                    Span(sb, text, start, i, Number);
                    continue;
                }
                if (char.IsLetter(c) || c == '_') {
                    var start = i;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                    var word = text.Substring(start, i - start);
                    var next = i;
                    while (next < text.Length && text[next] == ' ') next++;
                    var call = next < text.Length && text[next] == '(';
                    string color;
                    if (afterDef) color = Function;
                    else if (Control.Contains(word)) color = Keyword;
                    else if (Declare.Contains(word)) color = Storage;
                    else if (VexTypes.Contains(word)) color = Type;
                    else if (word.Length > 1 && word.All(ch => char.IsUpper(ch) || char.IsDigit(ch) || ch == '_')) color = Constant;
                    else if (Builtin.Contains(word) || call) color = Function;
                    else color = null;
                    afterDef = word == "def" || word == "class";
                    if (color == null) Plainly(sb, word);
                    else Span(sb, text, start, i, color);
                    continue;
                }
                if (c != ' ' && c != '\n') afterDef = false;
                Plainly(sb, c.ToString());
                i++;
            }
            return sb.ToString();
        }

        private static void Span(StringBuilder sb, string text, int start, int end, string color) {
            var s = text.Substring(start, end - start);
            // Line by line, so a multi-line string doesn't swallow the newlines' layout.
            var lines = s.Split('\n');
            for (var i = 0; i < lines.Length; i++) {
                if (i > 0) sb.Append('\n');
                if (lines[i].Length == 0) continue;
                sb.Append("<color=").Append(color).Append("><noparse>").Append(lines[i]).Append("</noparse></color>");
            }
        }

        private static void Plainly(StringBuilder sb, string s) {
            if (s.IndexOf('<') >= 0 || s.IndexOf('>') >= 0) sb.Append("<noparse>").Append(s).Append("</noparse>");
            else sb.Append(s);
        }
    }
}
