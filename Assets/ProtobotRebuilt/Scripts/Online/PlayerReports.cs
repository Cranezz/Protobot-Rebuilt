using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Protobot.Builds;
using Protobot.Interface;
using TMPro;
using UnityEngine;

namespace Protobot.Online {
    /// <summary>
    /// V2: reporting a player (the lobby's player rows, the match's Players window). The report goes to the developer's
    /// Discord channel (BugReport.Webhook) as a zip: report.md (who, why, what they wrote, the match and version),
    /// match.csv (where every robot was 5 times a second, the host's movement checks, who left: OnlineMatch.LastRecording)
    /// and the reporter's session log. Without a webhook it's saved in Logs/Reports to send by hand. One report per
    /// player per session.
    /// </summary>
    public static class PlayerReports {
        public static readonly string[] Reasons = {
            "Cheating or a modified app", "Unfair play on purpose (pinning, ramming)", "Offensive name or behavior",
            "Leaving or ruining matches on purpose", "Something else",
        };

        private static readonly HashSet<string> reported = new();

        public static void Open(string playerId, string name) {
            if (string.IsNullOrEmpty(playerId)) return;
            if (reported.Contains(playerId)) {
                Interface.Interface.Say("You've already reported " + name + " this session", false);
                return;
            }
            var reason = 0;
            var details = "";
            var dialog = Dialog.Open("Report " + name, new Vector2(620f, 430f));
            Ui.Column(dialog.Body, 8f);
            var row = Dialog.Row(dialog.Body, "Reason", 40f, 110f);
            Popup.Choice(row, Reasons, 0, i => reason = i, 400f);
            Dialog.Heading(dialog.Body, "What happened? (when, and what they did)");
            var field = Ui.Field(dialog.Body, "Details", "", v => details = v);
            field.lineType = TMP_InputField.LineType.MultiLineNewline;
            field.onValueChanged.AddListener(v => details = v);
            field.Size(-1, 110f);
            var note = Ui.Text(dialog.Body, "Note", "Sent to the Protobot moderators with the match record (where every robot was, five times a second, "
                                                     + "and the host's checks of how each robot moved), your session log, both names and the version. "
                                                     + "People read every report: only report what really happened.", Theme.FontSmall, Theme.TextDim);
            note.textWrappingMode = TextWrappingModes.Normal;
            note.Size(-1, 54f);
            var cancel = Ui.Button(dialog.Footer, "Cancel", "Cancel", dialog.Close, false, 6f);
            cancel.Size(110f, 34f);
            var send = Ui.Button(dialog.Footer, "Send", "Send report", () => {
                if (reason == Reasons.Length - 1 && string.IsNullOrWhiteSpace(details)) {
                    Interface.Interface.Say("Say what happened first (a sentence is enough)", false);
                    return;
                }
                dialog.Close();
                Send(playerId, name, Reasons[reason], details);
            }, true, 6f);
            send.Size(140f, 34f);
        }

        private static void Send(string playerId, string name, string reason, string details) {
            reported.Add(playerId);
            ActionLog.Write("report", "reported " + name + " (" + playerId + "): " + reason);
            ActionLog.Flush();
            byte[] zip;
            try { zip = Make(playerId, name, reason, details); }
            catch (Exception e) {
                Interface.Interface.Say("Couldn't make the report: " + e.Message, false);
                return;
            }
            var session = MatchSessions.Current;
            var content = "**Player report** · " + reason + "\n> Reported: " + name + " (`" + playerId + "`)\n> By: " + OnlineServices.Name
                          + " (`" + OnlineServices.PlayerId + "`) · " + BuildInfo.Display + (session != null ? " · match `" + session.Id + "`" : "");
            var fileName = "Player report " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".zip";
            if (string.IsNullOrEmpty(BugReport.Webhook) || CrashGuard.Instance == null) {
                Save(zip, fileName);
                return;
            }
            CrashGuard.Instance.StartCoroutine(BugReport.Post(content, zip, fileName, ok => {
                if (ok) Interface.Interface.Announce("Sent your report. Thank you: the moderators will look at it.");
                else {
                    Interface.Interface.Say("Couldn't send the report: it's saved, its folder is open", false);
                    Save(zip, fileName);
                }
            }));
        }

        private static byte[] Make(string playerId, string name, string reason, string details) {
            var session = MatchSessions.Current;
            var md = new StringBuilder();
            md.AppendLine("# Player report");
            md.AppendLine();
            md.AppendLine("- Reported: " + name + " (" + playerId + ")");
            md.AppendLine("- By: " + OnlineServices.Name + " (" + OnlineServices.PlayerId + ")");
            md.AppendLine("- Reason: " + reason);
            md.AppendLine("- When: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            md.AppendLine("- Version: " + BuildInfo.Full + " (" + Application.version + ")");
            if (session != null) {
                md.AppendLine("- Match: \"" + session.Name + "\" (" + session.Id + "), host " + session.Host + ", " + session.PlayerCount + " players");
                foreach (var p in session.Players)
                    md.AppendLine("  - " + MatchSessions.Get(p, MatchSessions.NameKey) + " (" + p.Id + "), " + MatchSessions.Get(p, MatchSessions.TeamKey));
            }
            md.AppendLine();
            md.AppendLine("## What they wrote");
            md.AppendLine();
            md.AppendLine(string.IsNullOrWhiteSpace(details) ? "(nothing)" : details);
            md.AppendLine();
            md.AppendLine("match.csv: seat, kind (pos = the robot's middle in the field's frame, inches from the field's middle; check = the host's "
                          + "movement check; left), x, y. The seats are in the match's order (OnlineMatch: red Left/Bottom, blue Right/Top).");
            using var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true)) {
                Text(archive, "report.md", md.ToString());
                Text(archive, "match.csv", string.IsNullOrEmpty(OnlineMatch.LastRecording) ? "(no match played yet)" : OnlineMatch.LastRecording);
                if (!string.IsNullOrEmpty(ActionLog.CurrentFile) && File.Exists(ActionLog.CurrentFile)) {
                    using var from = new FileStream(ActionLog.CurrentFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var to = archive.CreateEntry("session.log").Open();
                    from.CopyTo(to);
                }
            }
            return stream.ToArray();
        }

        private static void Text(ZipArchive zip, string name, string text) {
            using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            w.Write(text);
        }

        private static void Save(byte[] zip, string fileName) {
            Directory.CreateDirectory(BugReport.Folder);
            var path = Path.Combine(BugReport.Folder, fileName);
            File.WriteAllBytes(path, zip);
            Application.OpenURL("file:///" + BugReport.Folder.Replace('\\', '/'));
            Interface.Interface.Announce("Saved your report: " + fileName + " (its folder is open: send that file to the moderators)");
        }
    }
}
