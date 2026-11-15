// Adds a project access rule: players can't write leaderboard scores directly (only Cloud Code, which checks runs).
// The result (never the token) goes to Temp/access_result.txt.
var project = UnityEditor.CloudProjectSettings.projectId;
var env = Unity.Services.Core.Editor.Environments.EnvironmentsApi.Instance.ActiveEnvironmentId;
var outPath = System.IO.Path.GetFullPath("Temp/access_result.txt");
System.IO.File.WriteAllText(outPath, "working");
var tokenTask = new Unity.Services.Core.Editor.AccessTokens().GetServicesGatewayTokenAsync();
System.Threading.Tasks.Task.Run(async () => {
    try {
        var token = await tokenTask;
        var url = "https://services.api.unity.com/access/v1/projects/" + project + "/resource-policy";
        var body = "{\"statements\":[{\"Sid\":\"deny-player-leaderboard-writes\",\"Action\":[\"Write\"],\"Effect\":\"Deny\",\"Principal\":\"Player\",\"Resource\":\"urn:ugs:leaderboards:/v1/projects/*/leaderboards/*/scores/players/*\",\"Version\":\"1.0.0\"}]}";
        using (var http = new System.Net.Http.HttpClient()) {
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var request = new System.Net.Http.HttpRequestMessage(new System.Net.Http.HttpMethod("PATCH"), url) { Content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json") };
            var response = await http.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            var check = await http.GetAsync(url);
            var now = await check.Content.ReadAsStringAsync();
            System.IO.File.WriteAllText(outPath, "PATCH " + (int)response.StatusCode + " " + text + "\nGET " + (int)check.StatusCode + " " + now);
        }
    }
    catch (System.Exception e) { System.IO.File.WriteAllText(outPath, "error " + e.GetType().Name + ": " + e.Message); }
});
return "started for project " + project + " environment " + env;
