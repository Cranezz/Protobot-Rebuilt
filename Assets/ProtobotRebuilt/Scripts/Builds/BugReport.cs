using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
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
    // Sent to the developer's Discord channel through a webhook when Webhook is set (an attachment up to 10 MB;
    // a bigger one goes without the builds), else saved and its folder opened to send by hand.
    public static class BugReport {
        /// <summary>A Discord webhook (Server Settings > Integrations > Webhooks > New Webhook > Copy Webhook URL) the
        /// reports are sent to; empty: they're saved only. Kept out of the source in Resources/Secrets/BugWebhook.txt
        /// (git-ignored, so a public repository doesn't show it; it's still built into the app).</summary>
        public static string Webhook {
            get {
                if (webhook == null) {
                    var file = Resources.Load<TextAsset>("Secrets/BugWebhook");
                    webhook = file != null ? file.text.Trim() : "";
                }
                return webhook;
            }
        }
        private static string webhook;

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
            var note = Ui.Text(dialog.Body, "Note", "Also in the report: what you did this session (every step, click and key, step by step), the errors, "
                                                   + BuildInfo.Display + " and your computer's OS and graphics card. Nothing else, and nothing is sent until you press Send.",
                               Theme.FontSmall, Theme.TextDim);
            note.textWrappingMode = TextWrappingModes.Normal;
            note.Size(-1, 52f);
            var folder = Ui.Button(dialog.Footer, "Folder", "Open reports folder", () => {
                Directory.CreateDirectory(Folder);
                Application.OpenURL("file:///" + Folder.Replace('\\', '/'));
            }, false, 6f);
            folder.Size(170f, 34f);
            var send = Ui.Button(dialog.Footer, "Send", string.IsNullOrEmpty(Webhook) ? "Save report" : "Send", () => {
                if (string.IsNullOrWhiteSpace(what)) {
                    Interface.Interface.Say("Say what happened first (a sentence is enough)", false);
                    return;
                }
                dialog.Close();
                var zip = Make(what, withBuild, withPicture);
                if (zip == null) return;
                if (!string.IsNullOrEmpty(Webhook) && CrashGuard.Instance != null) CrashGuard.Instance.StartCoroutine(Upload(zip, what));
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
                    Copy(zip, ActionLog.CurrentFile, "session.log");
                    Copy(zip, ActionLog.PreviousFile, "previous.log");
                    Copy(zip, Path.Combine(CrashGuard.LogFolder, "bugs.log"), "bugs.log");
                    if (!Application.isEditor) Copy(zip, Application.consoleLogPath, "player.log");
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

        private static void Copy(ZipArchive zip, string file, string name) {
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return;
            try {
                // (the log may be open for writing: read it shared)
                using var from = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var to = zip.CreateEntry(name).Open();
                from.CopyTo(to);
            }
            catch (Exception) { }
        }

        private static void Saved(string zip) {
            Application.OpenURL("file:///" + Path.GetDirectoryName(zip).Replace('\\', '/'));
            Interface.Interface.Announce("Saved your bug report: " + Path.GetFileName(zip) + " (its folder is open: send that file to the developer)");
        }

        private static IEnumerator Upload(string zip, string what) {
            var bytes = File.ReadAllBytes(zip);
            var name = Path.GetFileName(zip);
            if (bytes.Length > 9_500_000) {
                // Too big for Discord: again without the builds and picture.
                var light = Make(what + "\n\n(sent without the build: the full report is " + bytes.Length / 1_000_000 + " MB, saved on their computer)", false, false);
                if (light != null) bytes = File.ReadAllBytes(light);
            }
            var title = (what ?? "").Split('\n')[0];
            if (title.Length > 150) title = title.Substring(0, 150) + "…";
            var content = "**Bug report** · " + BuildInfo.Display + " · " + SystemInfo.operatingSystem + "\n> " + title.Replace("\"", "'");
            var payload = "{\"content\":" + Json(content) + "}";
            var form = new List<IMultipartFormSection> {
                new MultipartFormDataSection("payload_json", payload),
                new MultipartFormFileSection("files[0]", bytes, name, "application/zip"),
            };
            using var request = UnityWebRequest.Post(Webhook, form);
            request.timeout = 60;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success) Interface.Interface.Announce("Sent your bug report. Thank you!");
            else {
                Debug.LogWarning("[BugReport] Couldn't send: " + request.error);
                Interface.Interface.Say("Couldn't send the report (" + request.error + "): it's saved, its folder is open", false);
                Saved(zip);
            }
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
