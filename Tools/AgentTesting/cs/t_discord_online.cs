// Edit mode: sets up the Discord "Players online" channel's updater on the server (admin API, the editor's sign-in):
// - Cloud Save private custom data "secrets": discordBotToken (from C:/Dev/ProtobotRebuiltV2/Discord/bot_token.txt) and
//   discordOnlineChannel (server_state.json channels.online, made by `setup_server.py online`);
// - a Scheduler schedule "discord-online" (hourly: the shortest UGS allows) and a Trigger that runs Cloud Code
//   "Presence" on it (with no player: it counts again and renames the channel).
// Publish Presence.js itself with t_cc_publish.cs (PlayerPrefs "t_cc_names" = "Presence"). The token is never shown;
// the result (statuses) is in Temp/discord_online.txt.
var project = UnityEditor.CloudProjectSettings.projectId;
var env = Unity.Services.Core.Editor.Environments.EnvironmentsApi.Instance.ActiveEnvironmentId;
var outPath = System.IO.Path.GetFullPath("Temp/discord_online.txt");
System.IO.File.WriteAllText(outPath, "working");
var token = System.IO.File.ReadAllText("C:/Dev/ProtobotRebuiltV2/Discord/bot_token.txt").Trim();
var stateText = System.IO.File.ReadAllText("C:/Dev/ProtobotRebuiltV2/Discord/server_state.json");
var channel = System.Text.RegularExpressions.Regex.Match(stateText, "\"online\"\\s*:\\s*\"([0-9]+)\"").Groups[1].Value;
if (token.Length < 50 || token.Contains(" ")) return "the token file doesn't look like a token";
if (channel.Length == 0) return "no channels.online in server_state.json (run setup_server.py online first)";
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
            var r = await http.PostAsync(secrets, J("{\"key\":\"discordBotToken\",\"value\":\"" + token + "\"}"));
            log.AppendLine("secret discordBotToken " + (int)r.StatusCode);
            r = await http.PostAsync(secrets, J("{\"key\":\"discordOnlineChannel\",\"value\":\"" + channel + "\"}"));
            log.AppendLine("secret discordOnlineChannel " + (int)r.StatusCode + " " + await r.Content.ReadAsStringAsync());

            var sched = api + "/scheduler/v1/projects/" + project + "/environments/" + env + "/configs";
            var list = await http.GetAsync(sched);
            var listText = await list.Content.ReadAsStringAsync();
            log.AppendLine("schedules " + (int)list.StatusCode + " " + listText);
            if (list.IsSuccessStatusCode && !listText.Contains("\"discord-online\"")) {
                r = await http.PostAsync(sched, J("{\"name\":\"discord-online\",\"eventName\":\"discord-online\",\"type\":\"recurring\",\"schedule\":\"0 * * * *\",\"payloadVersion\":1,\"payload\":\"{}\"}"));
                log.AppendLine("schedule create " + (int)r.StatusCode + " " + await r.Content.ReadAsStringAsync());
            }

            var trig = api + "/triggers/v1/projects/" + project + "/environments/" + env + "/configs";
            list = await http.GetAsync(trig);
            listText = await list.Content.ReadAsStringAsync();
            log.AppendLine("triggers " + (int)list.StatusCode + " " + listText);
            if (list.IsSuccessStatusCode && !listText.Contains("\"discord-online\"")) {
                r = await http.PostAsync(trig, J("{\"name\":\"discord-online\",\"eventType\":\"com.unity.services.scheduler.discord-online.v1\",\"actionType\":\"cloud-code\",\"actionUrn\":\"urn:ugs:cloud-code:Presence\"}"));
                log.AppendLine("trigger create " + (int)r.StatusCode + " " + await r.Content.ReadAsStringAsync());
            }
        }
    }
    catch (System.Exception e) { log.AppendLine("error " + e.GetType().Name + ": " + e.Message); }
    System.IO.File.WriteAllText(outPath, log.ToString().Replace(token, "<token>"));
});
return "started (channel " + channel + ")";
