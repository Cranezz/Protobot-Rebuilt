using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Protobot.Interface;
using TMPro;
using UnityEngine;

namespace Protobot.Builds {
    // V2: keeping work safe and bugs reported.
    // - A bug log (persistentDataPath/Logs/bugs.log): every error with the time, version, computer, tool, build and
    //   the last 80 lines logged before it (repeats counted, not copied).
    // - A recovery copy of the open build (Recovery/recovery.pbb + .json: which build, when) every minute while it
    //   has unsaved changes, and at once when an error happens (in case it's about to crash). The user's own file
    //   is never touched.
    // - A marker while the app runs, removed when it closes normally: found at the next start, the app closed
    //   unexpectedly, and if there's a recovery copy it's offered ("saved 12 seconds before it closed"). Opening
    //   that build later offers it again while the copy is newer than the file.
    // - Help > Report a bug / Request a part: the report (with the log) is saved, copied, and opened as a new GitHub
    //   issue when the project's page is set (IssuesUrl).
    public class CrashGuard : MonoBehaviour {
        /// <summary>The GitHub "new issue" page reports open (empty: reports are saved and copied only).</summary>
        public const string IssuesUrl = "";

        public static string LogFolder => Path.Combine(Application.persistentDataPath, "Logs");
        public static string RecoveryFolder => Path.Combine(Application.persistentDataPath, "Recovery");
        private static string BugLog => Path.Combine(LogFolder, "bugs.log");
        private static string Marker => Path.Combine(LogFolder, "running.lock");
        private static string RecoveryFile => Path.Combine(RecoveryFolder, "recovery.pbb");
        private static string RecoveryInfo => Path.Combine(RecoveryFolder, "recovery.json");

        [Serializable]
        private class Info {
            public string buildPath, buildName, version;
            public long savedTicks;
        }

        private static CrashGuard instance;
        public static CrashGuard Instance => instance;
        private static readonly Queue<string> recent = new();
        private static readonly Dictionary<string, int> seen = new();
        private static volatile bool errorPending;
        private bool crashedLastTime;
        private float nextSave, nextErrorSave;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Make() {
            if (instance != null || !Application.isPlaying) return;
            instance = new GameObject("Crash Guard").AddComponent<CrashGuard>();
            DontDestroyOnLoad(instance.gameObject);
        }

        private void Awake() {
            try {
                Directory.CreateDirectory(LogFolder);
                crashedLastTime = File.Exists(Marker) && !Application.isEditor;
                File.WriteAllText(Marker, DateTime.Now.ToString("o") + " " + Application.version);
            }
            catch (Exception) { }
            Application.logMessageReceivedThreaded += OnLog;
            Application.quitting += OnQuit;
            nextSave = Time.realtimeSinceStartup + 60f;
        }

        private void Start() {
            if (crashedLastTime) Invoke(nameof(OfferAfterCrash), 1.5f);
        }

        private static void OnQuit() {
            try { if (File.Exists(Marker)) File.Delete(Marker); }
            catch (Exception) { }
        }

        private void OnDestroy() => Application.logMessageReceivedThreaded -= OnLog;

        // ------------------------------------------------------------------ the log

        private static void OnLog(string message, string stack, LogType type) {
            lock (recent) {
                recent.Enqueue(DateTime.Now.ToString("HH:mm:ss.fff") + " " + type + ": " + First(message, 300));
                while (recent.Count > 80) recent.Dequeue();
            }
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            errorPending = true;
            var key = First(message, 200) + "|" + First(stack, 200);
            lock (seen) {
                seen.TryGetValue(key, out var count);
                seen[key] = count + 1;
                if (count > 0) {
                    if (count == 1 || count == 10 || count == 100) Append("(repeated " + (count + 1) + " times) " + First(message, 200) + "\n");
                    return;
                }
            }
            var sb = new StringBuilder();
            sb.AppendLine("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + type);
            sb.AppendLine(message);
            if (!string.IsNullOrEmpty(stack)) sb.AppendLine(stack.TrimEnd());
            sb.AppendLine("-- context: " + Context());
            sb.AppendLine("-- before it:");
            lock (recent) foreach (var line in recent) sb.AppendLine("   " + line);
            Append(sb.ToString());
        }

        private static string context = "";

        private static string Context() => BuildInfo.Full + ", " + SystemInfo.operatingSystem + ", " + SystemInfo.graphicsDeviceName + context;

        private void LateUpdate() {
            // Things the log thread can't ask Unity for.
            if (Time.frameCount % 30 == 0)
                context = ", tool " + (ToolRail.Current ?? "-") + ", build \"" + ModelExport.BuildName() + "\", " + PartsManager.FindLoadedObjects().Count + " parts"
                          + (Motion.Sandbox.Running ? ", simulating" : "");
            var now = Time.realtimeSinceStartup;
            if (errorPending && now >= nextErrorSave) {
                errorPending = false;
                nextErrorSave = now + 10f;
                SaveRecovery(true);
            }
            if (now >= nextSave) {
                nextSave = now + 60f;
                SaveRecovery(false);
            }
        }

        private static void Append(string text) {
            try {
                Directory.CreateDirectory(LogFolder);
                var info = new FileInfo(BugLog);
                if (info.Exists && info.Length > 4_000_000) File.Copy(BugLog, BugLog + ".old", true);
                if (info.Exists && info.Length > 4_000_000) File.Delete(BugLog);
                File.AppendAllText(BugLog, text);
            }
            catch (Exception) { }
        }

        private static string First(string s, int n) => string.IsNullOrEmpty(s) ? "" : s.Length <= n ? s : s.Substring(0, n);

        // ------------------------------------------------------------------ recovery copies

        private static BuildsManager Manager => FindAnyObjectByType<BuildsManager>();

        private static void SaveRecovery(bool afterError) {
            try {
                var manager = Manager;
                if (manager == null || Motion.Sandbox.Running || PivotCamera.Main == null) return;
                if (!manager.HasUnsavedChanges()) return;
                var data = SceneBuild.ToBuildData();
                if (data.parts == null || data.parts.Length == 0) return;
                Directory.CreateDirectory(RecoveryFolder);
                BuildFile.Write(RecoveryFile, data);
                File.WriteAllText(RecoveryInfo, JsonUtility.ToJson(new Info {
                    buildPath = manager.buildPath, buildName = ModelExport.BuildName(), version = Application.version, savedTicks = DateTime.Now.Ticks
                }));
                if (afterError) Debug.Log("[CrashGuard] Saved a recovery copy after an error");
            }
            catch (Exception e) {
                Append("(couldn't save the recovery copy: " + e.Message + ")\n");
            }
        }

        private static Info ReadInfo() {
            try { return File.Exists(RecoveryInfo) && File.Exists(RecoveryFile) ? JsonUtility.FromJson<Info>(File.ReadAllText(RecoveryInfo)) : null; }
            catch (Exception) { return null; }
        }

        private void OfferAfterCrash() {
            var info = ReadInfo();
            var dialog = Dialog.Open("Protobot closed unexpectedly", new Vector2(560f, 260f));
            Ui.Column(dialog.Body, 8f);
            var text = info != null
                ? "Your build \"" + info.buildName + "\" was saved " + Ago(info) + " before it closed (a separate copy: your own file wasn't changed). Open the saved copy?"
                : "Nothing unsaved was lost. What happened is in the bug log (Help > Report a bug sends it).";
            var label = Ui.Text(dialog.Body, "Text", text, Theme.FontBody, Theme.Text);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.Size(-1, 90f);
            var report = Ui.Button(dialog.Footer, "Report", "Report it", () => {
                dialog.Close();
                ReportDialog(false);
            }, false, 6f);
            report.Size(110f, 34f);
            if (info != null) {
                var later = Ui.Button(dialog.Footer, "Later", "Not now", dialog.Close, false, 6f);
                later.Size(100f, 34f);
                var open = Ui.Button(dialog.Footer, "Open", "Open the saved copy", () => {
                    dialog.Close();
                    OpenRecovery(info);
                }, true, 6f);
                open.Size(180f, 34f);
            }
            else {
                var ok = Ui.Button(dialog.Footer, "OK", "OK", dialog.Close, true, 6f);
                ok.Size(90f, 34f);
            }
        }

        private static string Ago(Info info) {
            var span = DateTime.Now - new DateTime(info.savedTicks);
            return span.TotalMinutes < 1 ? (int)span.TotalSeconds + " seconds" : span.TotalHours < 1 ? (int)span.TotalMinutes + " minutes"
                : span.TotalDays < 1 ? (int)span.TotalHours + " hours" : (int)span.TotalDays + " days";
        }

        private static void OpenRecovery(Info info) {
            var data = BuildsManager.ParsePath(RecoveryFile);
            var manager = Manager;
            if (data == null || manager == null) {
                Interface.Interface.Say("Couldn't open the saved copy", false);
                return;
            }
            manager.LoadRecovered(data, info.buildPath ?? "");
            Interface.Interface.Announce("Opened the copy saved before Protobot closed: Save to keep it" + (string.IsNullOrEmpty(info.buildPath) ? " (it was never saved: pick a name)" : " in " + Path.GetFileName(info.buildPath)));
        }

        /// <summary>Opening a build: when there's a recovery copy of it newer than the file, offer that instead.</summary>
        public static bool OfferForOpen(string path, Action openFile) {
            var info = ReadInfo();
            // (a never-saved build's copy has no path: GetFullPath("") throws, which stopped every build opening)
            if (info == null || string.IsNullOrEmpty(path) || string.IsNullOrEmpty(info.buildPath)) return false;
            try {
                if (!string.Equals(Path.GetFullPath(info.buildPath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) return false;
            }
            catch (Exception) {
                return false;
            }
            if (File.Exists(path) && File.GetLastWriteTime(path).Ticks >= info.savedTicks) return false;
            var dialog = Dialog.Open("A newer copy of this build", new Vector2(560f, 240f));
            Ui.Column(dialog.Body, 8f);
            var label = Ui.Text(dialog.Body, "Text", "Protobot saved a copy of \"" + info.buildName + "\" " + Ago(info) + " ago with changes that weren't saved in the file (before it closed unexpectedly). Open that copy?", Theme.FontBody, Theme.Text);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.Size(-1, 80f);
            var file = Ui.Button(dialog.Footer, "File", "Open the file", () => {
                dialog.Close();
                openFile();
            }, false, 6f);
            file.Size(130f, 34f);
            var copy = Ui.Button(dialog.Footer, "Copy", "Open the saved copy", () => {
                dialog.Close();
                OpenRecovery(info);
            }, true, 6f);
            copy.Size(180f, 34f);
            return true;
        }

        // ------------------------------------------------------------------ reports

        public static void ReportDialog(bool partRequest) {
            // V2: a bug report is a zip with the session's action log, the build and a picture (BugReport).
            if (!partRequest) {
                if (instance != null) instance.StartCoroutine(BugReport.OpenAfterScreenshot(() => BugReport.Dialog()));
                else BugReport.Dialog();
                return;
            }
            var dialog = Dialog.Open(partRequest ? "Request a part" : "Report a bug", new Vector2(620f, partRequest ? 400f : 430f));
            Ui.Column(dialog.Body, 8f);
            string what = "", id = "", link = "";
            if (partRequest) {
                Dialog.Heading(dialog.Body, "The part you'd like added");
                TMP_InputField Line(string label, Action<string> set) {
                    var input = Ui.Field(Dialog.Row(dialog.Body, label, 36f, 120f), label, "", v => set(v));
                    input.Size(-1, 34f, 1f);
                    input.onValueChanged.AddListener(v => set(v));
                    return input;
                }
                Line("Name", v => what = v);
                Line("Part number", v => id = v);
                Line("Link", v => link = v);
            }
            else {
                Dialog.Heading(dialog.Body, "What happened, and what were you doing?");
                var field = Ui.Field(dialog.Body, "What happened", "", v => what = v);
                field.lineType = TMP_InputField.LineType.MultiLineNewline;
                field.Size(-1, 150f);
                field.onValueChanged.AddListener(v => what = v);
            }
            var note = Ui.Text(dialog.Body, "Note", partRequest
                ? "Saved with your request: Protobot's version. Nothing is sent until you send it."
                : "Saved with your report: the bug log (errors with what led to them), Protobot's version and your computer's OS and graphics card. Nothing is sent until you send it.", Theme.FontSmall, Theme.TextDim);
            note.textWrappingMode = TextWrappingModes.Normal;
            note.Size(-1, 40f);
            var folder = Ui.Button(dialog.Footer, "Folder", "Open log folder", () => Application.OpenURL("file:///" + LogFolder.Replace('\\', '/')), false, 6f);
            folder.Size(140f, 34f);
            var send = Ui.Button(dialog.Footer, "Send", string.IsNullOrEmpty(IssuesUrl) ? "Save report" : "Send to GitHub", () => {
                if (partRequest && string.IsNullOrWhiteSpace(what) && string.IsNullOrWhiteSpace(id)) {
                    Interface.Interface.Say("Give the part's name or number", false);
                    return;
                }
                dialog.Close();
                Send(partRequest, what, id, link);
            }, true, 6f);
            send.Size(150f, 34f);
        }

        private static void Send(bool partRequest, string what, string id, string link) {
            var title = partRequest ? "Part request: " + (string.IsNullOrWhiteSpace(what) ? id : what) : "Bug: " + First((what ?? "").Split('\n')[0], 80);
            var body = new StringBuilder();
            if (partRequest) {
                body.AppendLine("**Part:** " + what);
                body.AppendLine("**VEX part number / ID:** " + id);
                body.AppendLine("**Link:** " + link);
                body.AppendLine();
                body.AppendLine("Protobot " + Application.version);
            }
            else {
                body.AppendLine(what);
                body.AppendLine();
                body.AppendLine("**Context:** " + Context());
                body.AppendLine();
                body.AppendLine("**Bug log (latest):**");
                body.AppendLine("```");
                try {
                    var log = File.Exists(BugLog) ? File.ReadAllText(BugLog) : "(no errors logged)";
                    body.AppendLine(log.Length > 5000 ? "…" + log.Substring(log.Length - 5000) : log);
                }
                catch (Exception) { body.AppendLine("(couldn't read the log)"); }
                body.AppendLine("```");
            }
            var folder = Path.Combine(LogFolder, "Reports");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, (partRequest ? "Part request " : "Bug report ") + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".md");
            File.WriteAllText(file, "# " + title + "\n\n" + body);
            GUIUtility.systemCopyBuffer = "# " + title + "\n\n" + body;
            if (!string.IsNullOrEmpty(IssuesUrl)) {
                var text = body.ToString();
                if (text.Length > 6000) text = text.Substring(0, 6000) + "\n…(the rest was copied: paste it in)";
                Application.OpenURL(IssuesUrl + "?title=" + Uri.EscapeDataString(title) + "&body=" + Uri.EscapeDataString(text) + "&labels=" + (partRequest ? "part%20request" : "bug"));
                Interface.Interface.Announce("Opened a new GitHub issue with your " + (partRequest ? "request" : "report") + " (it's copied too, and saved in Logs\\Reports)");
            }
            else Interface.Interface.Announce("Saved your " + (partRequest ? "part request" : "bug report") + " (copied too): " + Path.GetFileName(file) + ", in the log folder's Reports");
        }
    }
}
