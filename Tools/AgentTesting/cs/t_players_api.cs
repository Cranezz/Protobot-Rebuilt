// (Edit or Play mode) Tries Unity's Player Management admin API: how many players (and accounts) the project has.
// Result (never the token) in Temp/players_api.txt.
var project = UnityEditor.CloudProjectSettings.projectId;
var outPath = System.IO.Path.GetFullPath("Temp/players_api.txt");
System.IO.File.WriteAllText(outPath, "working");
var tokenTask = new Unity.Services.Core.Editor.AccessTokens().GetServicesGatewayTokenAsync();
System.Threading.Tasks.Task.Run(async () => {
    var log = new System.Text.StringBuilder();
    try {
        var token = await tokenTask;
        using (var http = new System.Net.Http.HttpClient()) {
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            foreach (var url in new[] {
                "https://services.api.unity.com/player-identity/v1/projects/" + project + "/users?limit=5",
                "https://services.api.unity.com/player-identity/v1/projects/" + project + "/users/count",
            }) {
                var r = await http.GetAsync(url);
                var body = await r.Content.ReadAsStringAsync();
                log.AppendLine(url.Replace(project, "<project>") + " -> " + (int)r.StatusCode + " " + (body.Length > 1500 ? body.Substring(0, 1500) : body));
            }
        }
    }
    catch (System.Exception e) { log.AppendLine("error " + e.GetType().Name + ": " + e.Message); }
    System.IO.File.WriteAllText(outPath, log.ToString());
});
return "started";
