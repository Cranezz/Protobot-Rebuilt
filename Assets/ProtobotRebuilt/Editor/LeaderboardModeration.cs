using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Protobot.EditorTools {
    /// <summary>
    /// V2: Tools > Protobot > Skills Leaderboard Moderation: the skills leaderboard's top runs (Unity's admin API, with
    /// the editor's own sign-in), and Remove for a run that breaks a major rule (a report in the Discord channel names
    /// the player id). Removing takes the score off and deletes the run's shared replay. V2: also finishes players' "Delete my
    /// online data" (their scores) and keeps the reports' Discord webhook on the server (Cloud Save private data).
    /// </summary>
    public class LeaderboardModeration : EditorWindow {
        // V2 (Phase 9): Driving Skills and Programming Skills boards, each with its own shared replay.
        private static readonly string[] Boards = { "skills-override", "programming-skills-override" };
        private static readonly string[] ReplayKeys = { "skills-replay", "programming-replay" };
        private int board;
        private string Board => Boards[board];
        private readonly List<(string id, string name, double score, string when)> rows = new();
        private string status = "", manualId = "", webhook = "";
        private int deletions = -1;
        private bool busy;
        private Vector2 scroll;

        [MenuItem("Tools/Protobot/Skills Leaderboard Moderation")]
        private static void Open() {
            var window = GetWindow<LeaderboardModeration>("Leaderboard moderation");
            window.minSize = new Vector2(520f, 360f);
            window.Load();
        }

        private static string Project => CloudProjectSettings.projectId;
        // (The services' editor assemblies aren't referenced by Assembly-CSharp-Editor: reached by reflection.)
        private static Type Find(string name) {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) {
                var t = a.GetType(name);
                if (t != null) return t;
            }
            return null;
        }

        private static string Environment {
            get {
                var api = Find("Unity.Services.Core.Editor.Environments.EnvironmentsApi");
                var instance = api?.GetProperty("Instance")?.GetValue(null);
                return instance?.GetType().GetProperty("ActiveEnvironmentId")?.GetValue(instance)?.ToString();
            }
        }

        private static async Task<HttpClient> Client() {
            var tokens = Activator.CreateInstance(Find("Unity.Services.Core.Editor.AccessTokens"));
            var token = await (Task<string>)tokens.GetType().GetMethod("GetServicesGatewayTokenAsync").Invoke(tokens, null);
            var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return http;
        }

        private async void Load() {
            busy = true;
            status = "Loading…";
            try {
                using var http = await Client();
                var url = "https://services.api.unity.com/leaderboards/v1/projects/" + Project + "/environments/" + Environment + "/leaderboards/" + Board + "/scores?limit=100";
                var response = await http.GetAsync(url);
                var text = await response.Content.ReadAsStringAsync();
                rows.Clear();
                if (!response.IsSuccessStatusCode) status = (int)response.StatusCode + " " + text;
                else {
                    var page = JsonUtility.FromJson<Page>(text);
                    foreach (var r in page.results) rows.Add((r.playerId, r.playerName, r.score, r.updatedTime));
                    status = rows.Count + " runs";
                }
            }
            catch (Exception e) { status = e.Message; }
            busy = false;
            Repaint();
        }

        private async void Remove(string playerId) {
            if (!EditorUtility.DisplayDialog("Remove the run", "Take player " + playerId + "'s run off the " + (board == 1 ? "programming " : "") + "skills leaderboard and delete its replay? This can't be undone.", "Remove", "Cancel")) return;
            busy = true;
            try {
                using var http = await Client();
                var score = await http.DeleteAsync("https://services.api.unity.com/leaderboards/v1/projects/" + Project + "/environments/" + Environment + "/leaderboards/" + Board + "/scores/players/" + playerId);
                var replay = await http.DeleteAsync("https://services.api.unity.com/cloud-save/v1/data/projects/" + Project + "/environments/" + Environment + "/players/" + playerId + "/public/items/" + ReplayKeys[board]);
                status = "Removed: score " + (int)score.StatusCode + ", replay " + (int)replay.StatusCode;
            }
            catch (Exception e) { status = e.Message; }
            busy = false;
            Load();
        }

        private static string CloudSave => "https://services.api.unity.com/cloud-save/v1/data/projects/" + Project + "/environments/" + Environment;

        // V2: Cloud Save custom data through the admin API (private = only servers read it).
        private static async Task<List<string>> ReadList(HttpClient http, string customId, string key, bool isPrivate) {
            var response = await http.GetAsync(CloudSave + "/custom/" + customId + (isPrivate ? "/private/items" : "/items") + "?keys=" + key);
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new Exception((int)response.StatusCode + " " + text);
            var list = new List<string>();
            var items = JsonUtility.FromJson<ListItems>(text);
            if (items?.results != null)
                foreach (var item in items.results)
                    if (item.key == key && item.value != null) list.AddRange(item.value);
            return list;
        }

        private static async Task Write(HttpClient http, string customId, string key, string valueJson, bool isPrivate) {
            var body = new StringContent("{\"key\":\"" + key + "\",\"value\":" + valueJson + "}", System.Text.Encoding.UTF8, "application/json");
            var response = await http.PostAsync(CloudSave + "/custom/" + customId + (isPrivate ? "/private/items" : "/items"), body);
            if (!response.IsSuccessStatusCode) throw new Exception((int)response.StatusCode + " " + await response.Content.ReadAsStringAsync());
        }

        private static string JsonList(IEnumerable<string> ids) {
            var parts = new List<string>();
            foreach (var id in ids) parts.Add("\"" + id.Replace("\"", "") + "\"");
            return "[" + string.Join(",", parts) + "]";
        }

        private static string JsonString(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        /// <summary>V2: where the app's reports go (Cloud Code "SendReport" reads it; the app never has it). From scripts:
        /// SetWebhook(url).</summary>
        public static Task<string> SetWebhook(string url) => SetWebhook(url, "discordWebhook");

        /// <summary>V2: a webhook for one kind of report ("webhook_bug", "webhook_report", "webhook_appeal").</summary>
        public static async Task<string> SetWebhook(string url, string key) {
            url = (url ?? "").Trim();
            if (!url.StartsWith("https://discord.com/api/webhooks/") && !url.StartsWith("https://discordapp.com/api/webhooks/"))
                return "That isn't a Discord webhook URL (https://discord.com/api/webhooks/...).";
            try {
                using var http = await Client();
                await Write(http, "secrets", key, JsonString(url), true);
                return null;
            }
            catch (Exception e) { return e.Message; }
        }

        private async void CountDeletions() {
            try {
                using var http = await Client();
                deletions = (await ReadList(http, "moderation", "deletions", true)).Count;
            }
            catch (Exception) { deletions = -1; }
            Repaint();
        }

        // V2: players who deleted their online data: their leaderboard scores (a script can't delete those) and replays.
        private async void FinishDeletions() {
            busy = true;
            try {
                using var http = await Client();
                var ids = await ReadList(http, "moderation", "deletions", true);
                var done = new List<string>();
                foreach (var id in ids) {
                    var ok = true;
                    for (var b = 0; b < Boards.Length; b++) {
                        var score = await http.DeleteAsync("https://services.api.unity.com/leaderboards/v1/projects/" + Project + "/environments/" + Environment + "/leaderboards/" + Boards[b] + "/scores/players/" + id);
                        if (!score.IsSuccessStatusCode && (int)score.StatusCode != 404) ok = false;
                        await http.DeleteAsync(CloudSave + "/players/" + id + "/public/items/" + ReplayKeys[b]);
                    }
                    if (ok) done.Add(id);
                }
                var left = ids.FindAll(id => !done.Contains(id));
                await Write(http, "moderation", "deletions", JsonList(left), true);
                var hidden = await ReadList(http, "leaderboard", "hidden", false);
                await Write(http, "leaderboard", "hidden", JsonList(hidden.FindAll(id => !done.Contains(id))), false);
                status = "Finished " + done.Count + " deletion" + (done.Count == 1 ? "" : "s") + (left.Count > 0 ? ", " + left.Count + " failed (try again)" : "");
                deletions = left.Count;
            }
            catch (Exception e) { status = e.Message; }
            busy = false;
            Load();
        }

        private void OnGUI() {
            EditorGUILayout.HelpBox("Runs on the Override skills leaderboard. Remove takes a run off (a major rule broken) and deletes its replay; "
                                    + "warnings and appeals are answered in the Discord channel.", MessageType.Info);
            var chosen = GUILayout.Toolbar(board, new[] { "Driving Skills", "Programming Skills" });
            if (chosen != board && !busy) {
                board = chosen;
                Load();
            }
            using (new EditorGUILayout.HorizontalScope()) {
                GUI.enabled = !busy;
                if (GUILayout.Button("Refresh", GUILayout.Width(90f))) Load();
                GUILayout.Label(status);
                GUI.enabled = true;
            }
            // V2: deletion requests and the reports' webhook.
            if (deletions < 0 && !busy) {
                deletions = 0;
                CountDeletions();
            }
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.Label("Deleted players to finish: " + deletions, GUILayout.Width(220f));
                GUI.enabled = !busy && deletions > 0;
                if (GUILayout.Button("Finish deletions", GUILayout.Width(130f))) FinishDeletions();
                GUI.enabled = true;
            }
            using (new EditorGUILayout.HorizontalScope()) {
                webhook = EditorGUILayout.PasswordField("Report webhook", webhook);
                GUI.enabled = !busy && webhook.Trim().Length > 0;
                if (GUILayout.Button("Save on server", GUILayout.Width(120f))) {
                    var url = webhook;
                    webhook = "";
                    _ = SetWebhook(url).ContinueWith(t => {
                        status = t.Result ?? "Webhook saved on the server (reports go to it now)";
                        EditorApplication.delayCall += Repaint;
                    });
                }
                GUI.enabled = true;
            }
            using (new EditorGUILayout.HorizontalScope()) {
                manualId = EditorGUILayout.TextField("Player id", manualId);
                GUI.enabled = !busy && manualId.Trim().Length > 0;
                if (GUILayout.Button("Remove", GUILayout.Width(80f))) Remove(manualId.Trim());
                GUI.enabled = true;
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (var i = 0; i < rows.Count; i++) {
                var r = rows[i];
                using (new EditorGUILayout.HorizontalScope()) {
                    GUILayout.Label("#" + (i + 1), GUILayout.Width(36f));
                    GUILayout.Label(r.name, GUILayout.Width(170f));
                    GUILayout.Label(r.score.ToString("0") + " pts", GUILayout.Width(60f));
                    EditorGUILayout.SelectableLabel(r.id, GUILayout.Height(18f));
                    GUI.enabled = !busy;
                    if (GUILayout.Button("Remove", GUILayout.Width(70f))) Remove(r.id);
                    GUI.enabled = true;
                }
            }
            EditorGUILayout.EndScrollView();
        }

        [Serializable] private class Row { public string playerId, playerName, updatedTime; public double score; }
        [Serializable] private class ListItem { public string key; public string[] value; }
        [Serializable] private class ListItems { public ListItem[] results = new ListItem[0]; }
        [Serializable] private class Page { public Row[] results = new Row[0]; }
    }
}
