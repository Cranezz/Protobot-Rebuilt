// Edit mode: puts the Discord channel #player-messages' id (server_state.json channels.console, made by
// `setup_server.py console`) into Cloud Save private custom data "secrets"/"discordConsoleChannel", where Presence.js
// (commands) and Feedback.js (answers) read it. The bot token is already there (t_discord_online.cs). Result in
// Temp/discord_console.txt. PlayerPrefs "t_dev_players" (player ids, commas), when set, becomes "secrets"/"devPlayers":
// the players `!test ask|notify` messages reach (the developer's own Protobot).
var project = UnityEditor.CloudProjectSettings.projectId;
var env = Unity.Services.Core.Editor.Environments.EnvironmentsApi.Instance.ActiveEnvironmentId;
var outPath = System.IO.Path.GetFullPath("Temp/discord_console.txt");
System.IO.File.WriteAllText(outPath, "working");
var stateText = System.IO.File.ReadAllText("C:/Dev/ProtobotRebuiltV2/Discord/server_state.json");
var channel = System.Text.RegularExpressions.Regex.Match(stateText, "\"console\"\\s*:\\s*\"([0-9]+)\"").Groups[1].Value;
if (channel.Length == 0) return "no channels.console in server_state.json (run setup_server.py console first)";
var devs = UnityEngine.PlayerPrefs.GetString("t_dev_players", "");
var gatewayTask = new Unity.Services.Core.Editor.AccessTokens().GetServicesGatewayTokenAsync();
System.Threading.Tasks.Task.Run(async () => {
    var log = new System.Text.StringBuilder();
    try {
        var bearer = await gatewayTask;
        using (var http = new System.Net.Http.HttpClient()) {
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
            var secrets = "https://services.api.unity.com/cloud-save/v1/data/projects/" + project + "/environments/" + env + "/custom/secrets/private/items";
            var r = await http.PostAsync(secrets, new System.Net.Http.StringContent("{\"key\":\"discordConsoleChannel\",\"value\":\"" + channel + "\"}", System.Text.Encoding.UTF8, "application/json"));
            log.AppendLine("secret discordConsoleChannel " + (int)r.StatusCode + " " + await r.Content.ReadAsStringAsync());
            if (devs.Length > 0) {
                r = await http.PostAsync(secrets, new System.Net.Http.StringContent("{\"key\":\"devPlayers\",\"value\":\"" + devs.Replace("\"", "") + "\"}", System.Text.Encoding.UTF8, "application/json"));
                log.AppendLine("secret devPlayers " + (int)r.StatusCode + " " + devs);
            }
        }
    }
    catch (System.Exception e) { log.AppendLine("error " + e.GetType().Name + ": " + e.Message); }
    System.IO.File.WriteAllText(outPath, log.ToString());
});
return "started (channel " + channel + ")";
