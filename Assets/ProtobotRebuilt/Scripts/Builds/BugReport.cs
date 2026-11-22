using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Protobot.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace Protobot.Builds {
    // V2: a bug report with everything needed to see what happened and make it happen again, in one zip
    // (persistentDataPath/Logs/Reports/Bug report <time>.zip):
    //   report.md         what the user wrote, the version and build number, the computer, the tool and build open
    //   session.log       this session's action log (ActionLog: every step, click, key, tool, message and error)
    //   previous.log      the session before (when the app closed unexpectedly, that's where it happened)
    //   bugs.log          errors with what led to them (CrashGuard)
    //   build.pbb         the build as it is now (opens in Protobot)
    //   saved.pbb         the build's file as last saved, if it has one
    //   screenshot.png    the screen when Report a bug was opened
    // Sent to the developer's Discord channel through the server (Send: Cloud Code "SendReport") (an attachment up to 10 MB;
    // a bigger one goes without the builds), else saved and its folder opened to send by hand.
    public static class BugReport {
        /// <summary>V2: reports go to the developer's Discord channel through the server (Cloud Code "SendReport", which
        /// holds the webhook and limits how many each player sends), never straight from the app: a webhook built into
        /// the app could be pulled out of it and used by anyone. The zip goes up as a Cloud Save file of the player's own,
        /// which the script attaches and then deletes. False when this copy has no online services (it's saved only).</summary>
        public static bool CanSend => !string.IsNullOrEmpty(Application.cloudProjectId);

        public static string Folder => Path.Combine(CrashGuard.LogFolder, "Reports");
        private static byte[] screenshot;

        /// <summary>Opens the report window after taking a picture of the screen as it is.</summary>
        public static IEnumerator OpenAfterScreenshot(Action open) {
            yield return new WaitForEndOfFrame();
            try {
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                screenshot = texture.EncodeToJPG(80);
                UnityEngine.Object.Destroy(texture);
            }
            catch (Exception) { screenshot = null; }
            open();
        }

        public static void Dialog(string what = "") {
            var dialog = Interface.Dialog.Open("Report a bug", new Vector2(640f, 470f));
            Ui.Column(dialog.Body, 8f);
            Interface.Dialog.Heading(dialog.Body, "What happened, and what were you doing?");
            var field = Ui.Field(dialog.Body, "What happened", what, v => what = v);
            field.lineType = TMP_InputField.LineType.MultiLineNewline;
            field.Size(-1, 140f);
            field.onValueChanged.AddListener(v => what = v);
            var withBuild = true;
            var withPicture = screenshot != null;
            ToolOptions.Switch((RectTransform)dialog.Body, "Include my build (so it can be opened and tried)", true, v => withBuild = v);
            if (screenshot != null) ToolOptions.Switch((RectTransform)dialog.Body, "Include a picture of the screen", true, v => withPicture = v);
            // V2: exactly what goes in it (the logs have the computer's user name taken out).
            var note = Ui.Text(dialog.Body, "Note", "Also in the report: what you did this session (each step, click and key; not what you type in text boxes), "
                                                   + "the errors and the app's log (with your computer's user name taken out), " + BuildInfo.Display
                                                   + ", and your computer's OS, processor, memory and graphics card. Please don't write your name, email or other "
                                                   + "personal details in it. Nothing is sent until you press Send.",
                               Theme.FontSmall, Theme.TextDim);
            note.textWrappingMode = TextWrappingModes.Normal;
            note.Size(-1, 70f);
            var folder = Ui.Button(dialog.Footer, "Folder", "Open reports folder", () => {
                Directory.CreateDirectory(Folder);
                Application.OpenURL("file:///" + Folder.Replace('\\', '/'));
            }, false, 6f);
            folder.Size(170f, 34f);
            var send = Ui.Button(dialog.Footer, "Send", !CanSend ? "Save report" : "Send", () => {
                if (string.IsNullOrWhiteSpace(what)) {
                    Interface.Interface.Say("Say what happened first (a sentence is enough)", false);
                    return;
                }
                dialog.Close();
                var zip = Make(what, withBuild, withPicture);
                if (zip == null) return;
                if (CanSend) Upload(zip, what);
                else Saved(zip);
            }, true, 6f);
            send.Size(130f, 34f);
        }

        /// <summary>Makes the zip; null when it couldn't.</summary>
        public static string Make(string what, bool withBuild, bool withPicture) {
            try {
                ActionLog.Write("report", "bug report made: " + what);
                ActionLog.Flush();
                Directory.CreateDirectory(Folder);
                var path = Path.Combine(Folder, "Bug report " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".zip");
                using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) {
                    Text(zip, "report.md", Summary(what));
                    Copy(zip, ActionLog.CurrentFile, "session.log", true);
                    Copy(zip, ActionLog.PreviousFile, "previous.log", true);
                    Copy(zip, Path.Combine(CrashGuard.LogFolder, "bugs.log"), "bugs.log", true);
                    if (!Application.isEditor) Copy(zip, Application.consoleLogPath, "player.log", true);
                    if (withBuild) {
                        var data = SceneBuild.ToBuildData();
                        if (data?.parts != null && data.parts.Length > 0) {
                            var temp = Path.Combine(Application.temporaryCachePath, "report build.pbb");
                            BuildFile.Write(temp, data);
                            Copy(zip, temp, "build.pbb");
                            File.Delete(temp);
                        }
                        var manager = UnityEngine.Object.FindAnyObjectByType<BuildsManager>();
                        if (manager != null && !string.IsNullOrEmpty(manager.buildPath)) Copy(zip, manager.buildPath, "saved.pbb");
                    }
                    if (withPicture && screenshot != null) {
                        using var s = zip.CreateEntry("screenshot.jpg").Open();
                        s.Write(screenshot, 0, screenshot.Length);
                    }
                }
                return path;
            }
            catch (Exception e) {
                Debug.LogWarning("[BugReport] Couldn't make the report: " + e.Message);
                Interface.Interface.Say("Couldn't make the report: " + e.Message, false);
                return null;
            }
        }

        private static string Summary(string what) {
            var sb = new StringBuilder();
            sb.AppendLine("# Bug report");
            sb.AppendLine();
            sb.AppendLine(what);
            sb.AppendLine();
            sb.AppendLine("- Version: " + BuildInfo.Full + " (" + Application.version + ")");
            sb.AppendLine("- Computer: " + SystemInfo.operatingSystem + ", " + SystemInfo.processorType + ", " + SystemInfo.systemMemorySize + " MB, "
                          + SystemInfo.graphicsDeviceName + " (" + SystemInfo.graphicsDeviceType + ", " + SystemInfo.graphicsMemorySize + " MB), " + Screen.width + "x" + Screen.height);
            sb.AppendLine("- When: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ", " + (int)Time.realtimeSinceStartup + " s into the session");
            sb.AppendLine("- Tool: " + (ToolRail.Current ?? "-") + ", build \"" + ModelExport.BuildName() + "\", " + PartsManager.FindLoadedObjects().Count + " parts"
                          + (Motion.Sandbox.Running ? ", simulating" : "") + (Game.SkillsGame.Active ? ", in a game" : ""));
            if (GraphicsQuality.GraphicsSettingsManager.Current != null) sb.AppendLine("- Graphics: " + GraphicsQuality.GraphicsSettingsManager.Current.preset);
            sb.AppendLine();
            sb.AppendLine("To see it again: Tools > Protobot > Open Bug Report… (in Play mode it opens build.pbb; session.log has every step).");
            return sb.ToString();
        }

        private static void Text(ZipArchive zip, string name, string text) {
            using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            w.Write(text);
        }

        private static void Copy(ZipArchive zip, string file, string name, bool scrub = false) {
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return;
            try {
                // (the log may be open for writing: read it shared)
                using var from = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (scrub) {
                    using var reader = new StreamReader(from);
                    Text(zip, name, Scrub(reader.ReadToEnd()));
                    return;
                }
                using var to = zip.CreateEntry(name).Open();
                from.CopyTo(to);
            }
            catch (Exception) { }
        }

        /// <summary>V2: a log with the computer's user name and computer name taken out (they're in every file path:
        /// C:/Users/<name>/...), which is often a real name.</summary>
        public static string Scrub(string text) {
            if (string.IsNullOrEmpty(text)) return text;
            try {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(home)) {
                    text = Regex.Replace(text, Regex.Escape(home), "%USERPROFILE%", RegexOptions.IgnoreCase);
                    text = Regex.Replace(text, Regex.Escape(home.Replace('\\', '/')), "%USERPROFILE%", RegexOptions.IgnoreCase);
                }
                var user = Environment.UserName;
                if (!string.IsNullOrEmpty(user))
                    text = Regex.Replace(text, @"([\\/]Users[\\/])" + Regex.Escape(user) + @"(?=[\\/]|\b)", "$1<user>", RegexOptions.IgnoreCase);
                var machine = Environment.MachineName;
                if (!string.IsNullOrEmpty(machine) && machine.Length >= 3)
                    text = Regex.Replace(text, @"\b" + Regex.Escape(machine) + @"\b", "<computer>", RegexOptions.IgnoreCase);
            }
            catch (Exception) { }
            return text;
        }

        private static void Saved(string zip) {
            Application.OpenURL("file:///" + Path.GetDirectoryName(zip).Replace('\\', '/'));
            Interface.Interface.Announce("Saved your bug report: " + Path.GetFileName(zip) + " (its folder is open: send that file to the developer)");
        }

        private static async void Upload(string zip, string what) {
            try {
                var bytes = File.ReadAllBytes(zip);
                var name = Path.GetFileName(zip);
                if (bytes.Length > MaxFile) {
                    // Too big for Discord: again without the builds and picture.
                    var light = Make(what + "\n\n(sent without the build: the full report is " + bytes.Length / 1_000_000 + " MB, saved on their computer)", false, false);
                    if (light != null) bytes = File.ReadAllBytes(light);
                }
                var title = (what ?? "").Split('\n')[0];
                if (title.Length > 150) title = title.Substring(0, 150) + "…";
                var content = "**Bug report** · " + BuildInfo.Display + " · " + SystemInfo.operatingSystem + "\n> " + title.Replace("\"", "'");
                var problem = await Send("bug", content, bytes, name);
                if (problem == null) Interface.Interface.Announce("Sent your bug report. Thank you!");
                else {
                    Interface.Interface.Say("Couldn't send the report (" + problem + "): it's saved, its folder is open", false);
                    Saved(zip);
                }
            }
            catch (Exception e) {
                Debug.LogWarning("[BugReport] " + e.Message);
                Saved(zip);
            }
        }

        /// <summary>Discord takes files up to 10 MB.</summary>
        public const int MaxFile = 9_500_000;

        /// <summary>V2: a message with a file to the developer's Discord channel, through the server (player reports use it
        /// too). Null when it went, else why not.</summary>
        public static async Task<string> Send(string kind, string content, byte[] file, string fileName) {
            if (!CanSend) return "this copy has no online services";
            if (file != null && file.Length > MaxFile) return "the report is too big (" + file.Length / 1_000_000 + " MB)";
            if (!await Online.OnlineServices.Start()) return Online.OnlineServices.Problem;
            string key = null;
            try {
                if (file != null) {
                    key = "report-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + UnityEngine.Random.Range(1000, 10000);
                    await Unity.Services.CloudSave.CloudSaveService.Instance.Files.Player.SaveAsync(key, file);
                }
                var json = await Unity.Services.CloudCode.CloudCodeService.Instance.CallEndpointAsync("SendReport", new Dictionary<string, object> {
                    { "kind", kind }, { "content", content ?? "" }, { "file", key ?? "" }, { "fileName", fileName ?? "report.zip" },
                    { "build", BuildInfo.Build },
                });
                var result = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                if (result != null && result.TryGetValue("sent", out var sent) && sent is bool ok && ok) return null;
                return result != null && result.TryGetValue("reason", out var why) ? why?.ToString() : "the server didn't take it";
            }
            catch (Exception e) {
                Debug.LogWarning("[BugReport] Couldn't send: " + e.Message);
                if (key != null) {
                    try { await Unity.Services.CloudSave.CloudSaveService.Instance.Files.Player.DeleteAsync(key); }
                    catch (Exception) { }
                }
                return Online.OnlineServices.Explain(e);
            }
        }

        /// <summary>The same as <see cref="Send"/>, for coroutine callers: done(true) when it went.</summary>
        public static IEnumerator Post(string content, byte[] file, string fileName, Action<bool> done) => Post(content, file, fileName, "report", done);

        /// <summary>V2: <paramref name="kind"/> picks the staff channel ("report": player reports, "appeal": appeals).</summary>
        public static IEnumerator Post(string content, byte[] file, string fileName, string kind, Action<bool> done) {
            var task = Send(kind, content, file, fileName);
            while (!task.IsCompleted) yield return null;
            var ok = !task.IsFaulted && task.Result == null;
            if (!ok) Debug.LogWarning("[BugReport] Couldn't send: " + (task.IsFaulted ? task.Exception?.Message : task.Result));
            done?.Invoke(ok);
        }

        private static string Json(string s) {
            var sb = new StringBuilder("\"");
            foreach (var c in s) {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n");
                else if (c < 32) sb.Append(' ');
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
