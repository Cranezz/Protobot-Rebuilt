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
    /// the player id). Removing takes the score off and deletes the run's shared replay.
    /// </summary>
    public class LeaderboardModeration : EditorWindow {
        private const string Board = "skills-override";
        private readonly List<(string id, string name, double score, string when)> rows = new();
        private string status = "", manualId = "";
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
            if (!EditorUtility.DisplayDialog("Remove the run", "Take player " + playerId + "'s run off the skills leaderboard and delete its replay? This can't be undone.", "Remove", "Cancel")) return;
            busy = true;
            try {
                using var http = await Client();
                var score = await http.DeleteAsync("https://services.api.unity.com/leaderboards/v1/projects/" + Project + "/environments/" + Environment + "/leaderboards/" + Board + "/scores/players/" + playerId);
                var replay = await http.DeleteAsync("https://services.api.unity.com/cloud-save/v1/data/projects/" + Project + "/environments/" + Environment + "/players/" + playerId + "/public/items/skills-replay");
                status = "Removed: score " + (int)score.StatusCode + ", replay " + (int)replay.StatusCode;
            }
            catch (Exception e) { status = e.Message; }
            busy = false;
            Load();
        }

        private void OnGUI() {
            EditorGUILayout.HelpBox("Runs on the Override skills leaderboard. Remove takes a run off (a major rule broken) and deletes its replay; "
                                    + "warnings and appeals are answered in the Discord channel.", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope()) {
                GUI.enabled = !busy;
                if (GUILayout.Button("Refresh", GUILayout.Width(90f))) Load();
                GUILayout.Label(status);
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
        [Serializable] private class Page { public Row[] results = new Row[0]; }
    }
}
