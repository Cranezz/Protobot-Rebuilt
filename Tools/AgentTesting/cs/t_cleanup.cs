// Removes the cloud test player's leaderboard score and stats (admin APIs, the editor's sign-in). Result in Temp/cleanup_result.txt.
var player = "wXvKMS8jdtElpuoLAuRGoC5f12gm";
var project = UnityEditor.CloudProjectSettings.projectId;
var env = Unity.Services.Core.Editor.Environments.EnvironmentsApi.Instance.ActiveEnvironmentId;
var outPath = System.IO.Path.GetFullPath("Temp/cleanup_result.txt");
System.IO.File.WriteAllText(outPath, "working");
var tokenTask = new Unity.Services.Core.Editor.AccessTokens().GetServicesGatewayTokenAsync();
System.Threading.Tasks.Task.Run(async () => {
    var sb = new System.Text.StringBuilder();
    try {
        var token = await tokenTask;
        using (var http = new System.Net.Http.HttpClient()) {
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var lb = await http.DeleteAsync("https://services.api.unity.com/leaderboards/v1/projects/" + project + "/environments/" + env + "/leaderboards/skills-override/scores/players/" + player);
            sb.AppendLine("leaderboard score: " + (int)lb.StatusCode + " " + await lb.Content.ReadAsStringAsync());
            var cs = await http.DeleteAsync("https://services.api.unity.com/cloud-save/v1/data/projects/" + project + "/environments/" + env + "/players/" + player + "/protected/items/stats");
            sb.AppendLine("stats: " + (int)cs.StatusCode + " " + await cs.Content.ReadAsStringAsync());
            var check = await http.GetAsync("https://services.api.unity.com/leaderboards/v1/projects/" + project + "/environments/" + env + "/leaderboards/skills-override/scores");
            sb.AppendLine("board now: " + (int)check.StatusCode + " " + await check.Content.ReadAsStringAsync());
        }
    }
    catch (System.Exception e) { sb.AppendLine("error " + e.Message); }
    System.IO.File.WriteAllText(outPath, sb.ToString());
});
return "started";
