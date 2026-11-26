// Edit mode: sets up the Discord live skills leaderboard on the server (admin API, the editor's sign-in): Cloud Save
// private "secrets" discordLeaderboardChannel / discordLeaderboardMessage (server_state.json: channels.leaderboard,
// messages.leaderboard; the bot token is already there from t_discord_online.cs) and two Triggers running Cloud Code
// "DiscordLeaderboard": on every leaderboard score posted, and on the hourly "discord-online" schedule.
// Publish DiscordLeaderboard.js itself with t_cc_publish.cs (PlayerPrefs "t_cc_names" = "DiscordLeaderboard").
// Result in Temp/discord_lb.txt.
var project = UnityEditor.CloudProjectSettings.projectId;
var env = Unity.Services.Core.Editor.Environments.EnvironmentsApi.Instance.ActiveEnvironmentId;
var outPath = System.IO.Path.GetFullPath("Temp/discord_lb.txt");
System.IO.File.WriteAllText(outPath, "working");
var state = System.IO.File.ReadAllText("C:/Dev/ProtobotRebuiltV2/Discord/server_state.json");
var channels = System.Text.RegularExpressions.Regex.Match(state, "\"channels\"\\s*:\\s*\\{[^}]*\\}").Value;
var messages = System.Text.RegularExpressions.Regex.Match(state, "\"messages\"\\s*:\\s*\\{[^}]*\\}").Value;
var channel = System.Text.RegularExpressions.Regex.Match(channels, "\"leaderboard\"\\s*:\\s*\"([0-9]+)\"").Groups[1].Value;
var message = System.Text.RegularExpressions.Regex.Match(messages, "\"leaderboard\"\\s*:\\s*\"([0-9]+)\"").Groups[1].Value;
if (channel.Length == 0 || message.Length == 0) return "run setup_server.py channels leaderboard first";
var gatewayTask = new Unity.Services.Core.Editor.AccessTokens().GetServicesGatewayTokenAsync();
System.Threading.Tasks.Task.Run(async () => {
    var log = new System.Text.StringBuilder();
    try {
        var bearer = await gatewayTask;
        var api = "https://services.api.unity.com";
        using (var http = new System.Net.Http.HttpClient()) {
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
            System.Func<string, System.Net.Http.StringContent> J = s => new System.Net.Http.StringContent(s, System.Text.Encoding.UTF8, "application/json");
            var secrets = api + "/cloud-save/v1/data/projects/" + project + "/environments/" + env + "/custom/secrets/private/items";
            var r = await http.PostAsync(secrets, J("{\"key\":\"discordLeaderboardChannel\",\"value\":\"" + channel + "\"}"));
            log.AppendLine("secret channel " + (int)r.StatusCode);
            r = await http.PostAsync(secrets, J("{\"key\":\"discordLeaderboardMessage\",\"value\":\"" + message + "\"}"));
            log.AppendLine("secret message " + (int)r.StatusCode);
            var trig = api + "/triggers/v1/projects/" + project + "/environments/" + env + "/configs";
            var list = await http.GetAsync(trig);
            var listText = await list.Content.ReadAsStringAsync();
            log.AppendLine("triggers " + (int)list.StatusCode);
            foreach (var pair in new[] { new[] { "discord-leaderboard", "com.unity.services.leaderboards.score-submitted.v1" },
                                         new[] { "discord-leaderboard-hourly", "com.unity.services.scheduler.discord-online.v1" } }) {
                var name = pair[0];
                var eventType = pair[1];
                if (listText.Contains("\"" + name + "\"")) { log.AppendLine(name + " already there"); continue; }
                r = await http.PostAsync(trig, J("{\"name\":\"" + name + "\",\"eventType\":\"" + eventType + "\",\"actionType\":\"cloud-code\",\"actionUrn\":\"urn:ugs:cloud-code:DiscordLeaderboard\"}"));
                log.AppendLine(name + " create " + (int)r.StatusCode + " " + await r.Content.ReadAsStringAsync());
            }
        }
    }
    catch (System.Exception e) { log.AppendLine("error " + e.GetType().Name + ": " + e.Message); }
    System.IO.File.WriteAllText(outPath, log.ToString());
});
return "started (channel " + channel + ", message " + message + ")";
