using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Protobot.EditorTools {
    /// <summary>
    /// V2 (build 75): Tools > Protobot > Players, the developer's board (Unity's admin API with the editor's own
    /// sign-in; nothing in the app reads it): how many players there are (guests and accounts, from Player Management),
    /// how many are online now and were active today / this week (Cloud Code "Presence"'s heartbeats), the time spent in
    /// each part of the app, and per player: their name, account or guest, total time, today, the parts they use most,
    /// first and last seen. Usage is only kept from build 75 on, for players who agreed to the terms.
    /// </summary>
    public class PlayersBoard : EditorWindow {
        private class Row {
            public string id, name, username;
            public bool account;
            public double total, today;
            public Dictionary<string, double> areas = new();
            public DateTime? first, last, created;
        }

        private readonly List<Row> rows = new();
        private int players, accounts, online, today, week;
        private readonly Dictionary<string, double> areaTotals = new();
        private string status = "", filter = "";
        private bool busy, accountsOnly;
        private int sort;
        private Vector2 scroll;
        private static readonly string[] Sorts = { "Last seen", "Most time", "Newest" };

        [MenuItem("Tools/Protobot/Players")]
        private static void Open() {
            var window = GetWindow<PlayersBoard>("Players");
            window.minSize = new Vector2(760f, 420f);
            window.Load();
        }

        private static string Project => CloudProjectSettings.projectId;

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

        private static DateTime? Seconds(JToken t) => long.TryParse(t?.ToString(), out var s) && s > 0 ? DateTimeOffset.FromUnixTimeSeconds(s).LocalDateTime : null;
        private static DateTime? Millis(JToken t) => t != null && double.TryParse(t.ToString(), out var ms) && ms > 0 ? DateTimeOffset.FromUnixTimeMilliseconds((long)ms).LocalDateTime : null;

        // Every page of a list the admin API pages with "next" (Player Management) or links.next (Cloud Save).
        private static async Task<List<JToken>> All(HttpClient http, string url) {
            var all = new List<JToken>();
            string after = null;
            for (var page = 0; page < 200; page++) {
                var response = await http.GetAsync(after == null ? url : url + (url.Contains("?") ? "&" : "?") + "after=" + Uri.EscapeDataString(after));
                var text = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) throw new Exception((int)response.StatusCode + " " + text.Substring(0, Math.Min(200, text.Length)));
                var json = JObject.Parse(text);
                var results = json["results"] as JArray;
                if (results == null || results.Count == 0) break;
                all.AddRange(results);
                var next = json["next"]?.ToString();
                if (string.IsNullOrEmpty(next)) {
                    var link = json["links"]?["next"]?.ToString();
                    if (!string.IsNullOrEmpty(link)) {
                        var i = link.IndexOf("after=", StringComparison.Ordinal);
                        next = i >= 0 ? Uri.UnescapeDataString(link.Substring(i + 6).Split('&')[0]) : null;
                    }
                }
                if (string.IsNullOrEmpty(next)) break;
                after = next;
            }
            return all;
        }

        private async void Load() {
            busy = true;
            status = "Loading…";
            Repaint();
            try {
                using var http = await Client();
                var cloudSave = "https://services.api.unity.com/cloud-save/v1/data/projects/" + Project + "/environments/" + Environment;
                var users = await All(http, "https://services.api.unity.com/player-identity/v1/projects/" + Project + "/users?limit=100");
                var usage = await All(http, cloudSave + "/custom/usage/private/items");
                var presence = await All(http, cloudSave + "/custom/presence/private/items");
                var now = DateTime.Now;
                rows.Clear();
                areaTotals.Clear();
                var byId = new Dictionary<string, Row>();
                foreach (var u in users) {
                    var id = u["id"]?.ToString();
                    if (string.IsNullOrEmpty(id)) continue;
                    var login = u["usernamePasswordLoginMetadata"];
                    var row = new Row { id = id, account = login != null, username = login?["username"]?.ToString(), created = Seconds(u["createdAt"]), last = Seconds(u["lastLoginAt"]) };
                    byId[id] = row;
                }
                foreach (var item in usage) {
                    var id = item["key"]?.ToString();
                    var v = item["value"];
                    if (id == null || v == null) continue;
                    if (!byId.TryGetValue(id, out var row)) byId[id] = row = new Row { id = id };
                    row.name = v["name"]?.ToString();
                    row.account |= v["account"]?.ToObject<bool>() == true;
                    row.total = v["total"]?.ToObject<double>() ?? 0;
                    row.today = v["days"]?[now.ToUniversalTime().ToString("yyyy-MM-dd")]?.ToObject<double>() ?? 0;
                    if (v["areas"] is JObject areas)
                        foreach (var a in areas.Properties()) {
                            var s = a.Value.ToObject<double>();
                            row.areas[a.Name] = s;
                            areaTotals[a.Name] = (areaTotals.TryGetValue(a.Name, out var t) ? t : 0) + s;
                        }
                    row.first = Millis(v["first"]) ?? row.created;
                    var seen = Millis(v["last"]);
                    if (seen != null && (row.last == null || seen > row.last)) row.last = seen;
                }
                rows.AddRange(byId.Values);
                players = rows.Count;
                accounts = rows.Count(r => r.account);
                online = presence.Count(p => Millis(p["value"]) is DateTime at && (now - at).TotalSeconds < 150);
                today = rows.Count(r => r.last is DateTime l && l.Date == now.Date);
                week = rows.Count(r => r.last is DateTime l && (now - l).TotalDays < 7);
                status = "Loaded " + now.ToString("HH:mm:ss");
            }
            catch (Exception e) { status = "Couldn't load: " + e.Message; }
            busy = false;
            Repaint();
        }

        private static string Time(double s) {
            if (s < 60) return Math.Round(s) + " s";
            var m = s / 60;
            if (m < 60) return Math.Round(m) + " min";
            return Math.Floor(m / 60) + " h " + Math.Round(m % 60) + " min";
        }

        private static string When(DateTime? t) {
            if (t == null) return "—";
            var ago = DateTime.Now - t.Value;
            return ago.TotalMinutes < 2.5 ? "online now" : ago.TotalHours < 1 ? Math.Round(ago.TotalMinutes) + " min ago"
                : ago.TotalDays < 1 ? Math.Round(ago.TotalHours) + " h ago" : t.Value.ToString("MMM d, yyyy");
        }

        private void OnGUI() {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                GUI.enabled = !busy;
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70))) Load();
                GUI.enabled = true;
                GUILayout.Label("Sort", GUILayout.Width(30));
                sort = EditorGUILayout.Popup(sort, Sorts, EditorStyles.toolbarPopup, GUILayout.Width(100));
                accountsOnly = GUILayout.Toggle(accountsOnly, "Accounts only", EditorStyles.toolbarButton, GUILayout.Width(100));
                filter = GUILayout.TextField(filter, EditorStyles.toolbarSearchField, GUILayout.Width(180));
                GUILayout.FlexibleSpace();
                GUILayout.Label(status, EditorStyles.miniLabel);
            }
            var big = new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 };
            using (new EditorGUILayout.HorizontalScope()) {
                foreach (var (label, value) in new[] { ("Players", players), ("Accounts", accounts), ("Guests", players - accounts),
                                                       ("Online now", online), ("Active today", today), ("This week", week) })
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(115))) {
                        GUILayout.Label(label, EditorStyles.miniLabel);
                        GUILayout.Label(value.ToString("N0"), big);
                    }
            }
            if (areaTotals.Count > 0) {
                var total = areaTotals.Values.Sum();
                GUILayout.Label("Time spent by everyone: " + Time(total) + "   " + string.Join("   ",
                    areaTotals.OrderByDescending(a => a.Value).Select(a => a.Key + " " + Math.Round(100 * a.Value / Math.Max(1, total)) + "%")), EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox)) {
                GUILayout.Label("Player", EditorStyles.miniBoldLabel, GUILayout.Width(190));
                GUILayout.Label("Account", EditorStyles.miniBoldLabel, GUILayout.Width(110));
                GUILayout.Label("Total", EditorStyles.miniBoldLabel, GUILayout.Width(80));
                GUILayout.Label("Today", EditorStyles.miniBoldLabel, GUILayout.Width(70));
                GUILayout.Label("Uses it for", EditorStyles.miniBoldLabel, GUILayout.ExpandWidth(true));
                GUILayout.Label("Last seen", EditorStyles.miniBoldLabel, GUILayout.Width(100));
                GUILayout.Label("First seen", EditorStyles.miniBoldLabel, GUILayout.Width(90));
            }
            IEnumerable<Row> shown = rows;
            if (accountsOnly) shown = shown.Where(r => r.account);
            if (!string.IsNullOrWhiteSpace(filter))
                shown = shown.Where(r => (r.name ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                                         || (r.username ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 || r.id.StartsWith(filter));
            shown = sort == 1 ? shown.OrderByDescending(r => r.total) : sort == 2 ? shown.OrderByDescending(r => r.first ?? r.created ?? DateTime.MinValue)
                : shown.OrderByDescending(r => r.last ?? DateTime.MinValue);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var r in shown.Take(500)) {
                using (new EditorGUILayout.HorizontalScope()) {
                    var name = string.IsNullOrEmpty(r.name) ? "(not since build 75)" : r.name;
                    GUILayout.Label(new GUIContent(name, "Player id " + r.id), GUILayout.Width(190));
                    GUILayout.Label(r.account ? (r.username ?? "account") : "guest", GUILayout.Width(110));
                    GUILayout.Label(r.total > 0 ? Time(r.total) : "—", GUILayout.Width(80));
                    GUILayout.Label(r.today > 0 ? Time(r.today) : "—", GUILayout.Width(70));
                    var all = r.areas.Values.Sum();
                    GUILayout.Label(all > 0 ? string.Join(", ", r.areas.OrderByDescending(a => a.Value).Take(4).Select(a => a.Key + " " + Math.Round(100 * a.Value / all) + "%")) : "",
                        EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
                    GUILayout.Label(When(r.last), GUILayout.Width(100));
                    GUILayout.Label((r.first ?? r.created)?.ToString("MMM d") ?? "—", GUILayout.Width(90));
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.HelpBox("Time and what it's used for are kept from build 75 on, for players who agreed to the terms (Presence heartbeats). " +
                                    "Player counts come from Unity's player list (every guest and account ever made).", MessageType.None);
        }
    }
}
