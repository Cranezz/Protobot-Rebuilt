// Edit mode: tests the hourly "discord-online" path once: PlayerPrefs "t_ps" = "once" makes a one-time schedule of the
// same event 2 minutes from now (the trigger runs Cloud Code Presence with no player); "read" shows presence-stats
// (count/discord: when each was last worked out) and lists the schedules. Result in Temp/presence_sched.txt.
var project = UnityEditor.CloudProjectSettings.projectId;
var env = Unity.Services.Core.Editor.Environments.EnvironmentsApi.Instance.ActiveEnvironmentId;
var what = UnityEngine.PlayerPrefs.GetString("t_ps", "read");
var outPath = System.IO.Path.GetFullPath("Temp/presence_sched.txt");
System.IO.File.WriteAllText(outPath, "working");
var when = System.DateTime.UtcNow.AddMinutes(2).ToString("yyyy-MM-ddTHH:mm:00Z");
var gatewayTask = new Unity.Services.Core.Editor.AccessTokens().GetServicesGatewayTokenAsync();
System.Threading.Tasks.Task.Run(async () => {
    var log = new System.Text.StringBuilder();
    try {
        var bearer = await gatewayTask;
        var api = "https://services.api.unity.com";
        using (var http = new System.Net.Http.HttpClient()) {
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
            var sched = api + "/scheduler/v1/projects/" + project + "/environments/" + env + "/configs";
            if (what == "once") {
                var r = await http.PostAsync(sched, new System.Net.Http.StringContent("{\"name\":\"discord-online-test\",\"eventName\":\"discord-online\",\"type\":\"one-time\",\"schedule\":\"" + when + "\",\"payloadVersion\":1,\"payload\":\"{}\"}", System.Text.Encoding.UTF8, "application/json"));
                log.AppendLine("once at " + when + ": " + (int)r.StatusCode + " " + await r.Content.ReadAsStringAsync());
            }
            var stats = await http.GetAsync(api + "/cloud-save/v1/data/projects/" + project + "/environments/" + env + "/custom/presence-stats/private/items?keys=count&keys=discord&keys=scheduled");
            log.AppendLine("now " + System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + " stats " + (int)stats.StatusCode + " " + await stats.Content.ReadAsStringAsync());
            var list = await http.GetAsync(sched);
            log.AppendLine("schedules " + await list.Content.ReadAsStringAsync());
        }
    }
    catch (System.Exception e) { log.AppendLine("error " + e.GetType().Name + ": " + e.Message); }
    System.IO.File.WriteAllText(outPath, log.ToString());
});
return "started " + what;
