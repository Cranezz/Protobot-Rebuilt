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

        // ------------------------------------------------------------------ V2: leaderboard runs and appeals

        public static readonly string[] RunReasons = {
            "The score looks impossible", "The robot breaks the size or other rules", "Uses a glitch or exploit",
            "Made with a modified app", "Something else",
        };

        /// <summary>Reports a skills leaderboard run: the moderators get the run's replay with the report (fetched
        /// here if it wasn't watched), and decide (warning, or the run taken off the leaderboard).</summary>
        public static void OpenRun(string playerId, string name, int score, Game.ReplayFile replay) {
            if (string.IsNullOrEmpty(playerId)) return;
            if (reported.Contains("run:" + playerId)) {
                Interface.Interface.Say("You've already reported " + name + "'s run this session", false);
                return;
            }
            var reason = 0;
            var details = "";
            var dialog = Dialog.Open("Report " + name + "'s run (" + score + " points)", new Vector2(620f, 410f));
            Ui.Column(dialog.Body, 8f);
            var row = Dialog.Row(dialog.Body, "Reason", 40f, 110f);
            Popup.Choice(row, RunReasons, 0, i => reason = i, 400f);
            Dialog.Heading(dialog.Body, "What's wrong with it? (when in the run, what you saw)");
            var field = Ui.Field(dialog.Body, "Details", "", v => details = v);
            field.lineType = TMP_InputField.LineType.MultiLineNewline;
            field.onValueChanged.AddListener(v => details = v);
            field.Size(-1, 110f);
            var note = Ui.Text(dialog.Body, "Note", "Sent to the Protobot moderators with the run's replay and your session log. A run that breaks a "
                                                     + "major rule is taken off the leaderboard; a minor one gets a warning. Players can appeal.", Theme.FontSmall, Theme.TextDim);
            note.textWrappingMode = TextWrappingModes.Normal;
            note.Size(-1, 40f);
            var cancel = Ui.Button(dialog.Footer, "Cancel", "Cancel", dialog.Close, false, 6f);
            cancel.Size(110f, 34f);
            var send = Ui.Button(dialog.Footer, "Send", "Send report", async () => {
                if (string.IsNullOrWhiteSpace(details)) {
                    Interface.Interface.Say("Say what's wrong with the run first (a sentence is enough)", false);
                    return;
                }
                dialog.Close();
                reported.Add("run:" + playerId);
                replay ??= await Accounts.LoadReplay(playerId);
                var md = new StringBuilder();
                md.AppendLine("# Skills run report");
                md.AppendLine();
                md.AppendLine("- Run: " + name + " (" + playerId + "), " + score + " points");
                md.AppendLine("- By: " + OnlineServices.Name + " (" + OnlineServices.PlayerId + ")");
                md.AppendLine("- Reason: " + RunReasons[reason]);
                md.AppendLine("- When: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                md.AppendLine("- Version: " + BuildInfo.Full);
                md.AppendLine("- Replay: " + (replay != null ? "run.pbr (File > Open replay… in Protobot)" : "none shared"));
                md.AppendLine();
                md.AppendLine("## What they wrote");
                md.AppendLine();
                md.AppendLine(details);
                md.AppendLine();
                md.AppendLine("To take the run off: Unity editor, Tools > Protobot > Skills Leaderboard Moderation (player id above).");
                var content = "**Skills run report** · " + RunReasons[reason] + "\n> Run: " + name + " (`" + playerId + "`), " + score + " points\n> By: "
                              + OnlineServices.Name + " (`" + OnlineServices.PlayerId + "`) · " + BuildInfo.Display;
                Post(content, "Run report", md.ToString(), replay, "report " + name + "'s run (" + playerId + "): " + RunReasons[reason]);
            }, true, 6f);
            send.Size(140f, 34f);
        }

        /// <summary>Appeals a moderators' decision (a run taken off, a warning): it goes to the same channel.</summary>
        public static void OpenAppeal() {
            var details = "";
            var dialog = Dialog.Open("Appeal a decision", new Vector2(620f, 380f));
            Ui.Column(dialog.Body, 8f);
            var intro = Ui.Text(dialog.Body, "Intro", "If a run of yours was taken off the skills leaderboard, or you got a warning, and you think it was "
                                                       + "a mistake, say why. Your best run's replay goes with it.", Theme.FontBody, Theme.TextDim);
            intro.textWrappingMode = TextWrappingModes.Normal;
            intro.Size(-1, 48f);
            Dialog.Heading(dialog.Body, "Why should the decision change?");
            var field = Ui.Field(dialog.Body, "Details", "", v => details = v);
            field.lineType = TMP_InputField.LineType.MultiLineNewline;
            field.onValueChanged.AddListener(v => details = v);
            field.Size(-1, 120f);
            var cancel = Ui.Button(dialog.Footer, "Cancel", "Cancel", dialog.Close, false, 6f);
            cancel.Size(110f, 34f);
            var send = Ui.Button(dialog.Footer, "Send", "Send appeal", async () => {
                if (string.IsNullOrWhiteSpace(details)) {
                    Interface.Interface.Say("Say why first (a sentence is enough)", false);
                    return;
                }
                dialog.Close();
                var replay = Game.Replays.Last ?? await Accounts.LoadReplay(OnlineServices.PlayerId);
                var md = new StringBuilder();
                md.AppendLine("# Appeal");
                md.AppendLine();
                md.AppendLine("- From: " + OnlineServices.Name + " (" + OnlineServices.PlayerId + ")" + (Accounts.IsGuest ? ", guest" : ", account " + Accounts.Username));
                md.AppendLine("- When: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                md.AppendLine("- Version: " + BuildInfo.Full);
                md.AppendLine();
                md.AppendLine(details);
                var content = "**Appeal** from " + OnlineServices.Name + " (`" + OnlineServices.PlayerId + "`) · " + BuildInfo.Display;
                Post(content, "Appeal", md.ToString(), replay, "appeal sent");
            }, true, 6f);
            send.Size(140f, 34f);
        }

        private static void Post(string content, string kind, string markdown, Game.ReplayFile replay, string log) {
            ActionLog.Write("report", log);
            ActionLog.Flush();
            byte[] zip;
            try {
                using var stream = new MemoryStream();
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true)) {
                    Text(archive, "report.md", markdown);
                    if (replay != null) {
                        using var to = archive.CreateEntry("run.pbr").Open();
                        var bytes = Game.Replays.ToBytes(replay);
                        to.Write(bytes, 0, bytes.Length);
                    }
                    if (!string.IsNullOrEmpty(ActionLog.CurrentFile) && File.Exists(ActionLog.CurrentFile)) {
                        using var from = new FileStream(ActionLog.CurrentFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var log2 = archive.CreateEntry("session.log").Open();
                        from.CopyTo(log2);
                    }
                }
                zip = stream.ToArray();
            }
            catch (Exception e) {
                Interface.Interface.Say("Couldn't make the " + kind.ToLowerInvariant() + ": " + e.Message, false);
                return;
            }
            var fileName = kind + " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".zip";
            if (string.IsNullOrEmpty(BugReport.Webhook) || CrashGuard.Instance == null) {
                Save(zip, fileName);
                return;
            }
            CrashGuard.Instance.StartCoroutine(BugReport.Post(content, zip, fileName, ok => {
                if (ok) Interface.Interface.Announce("Sent. Thank you: the moderators will look at it.");
                else {
                    Interface.Interface.Say("Couldn't send it: it's saved, its folder is open", false);
                    Save(zip, fileName);
                }
            }));
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
