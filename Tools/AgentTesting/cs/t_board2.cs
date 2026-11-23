// Which admin call the Players board fails on: the first page of each list, status and the start of the answer.
var project = UnityEditor.CloudProjectSettings.projectId;
var env = Unity.Services.Core.Editor.Environments.EnvironmentsApi.Instance.ActiveEnvironmentId.ToString();
var outPath = System.IO.Path.GetFullPath("Temp/board2.txt");
var tokenTask = new Unity.Services.Core.Editor.AccessTokens().GetServicesGatewayTokenAsync();
System.Threading.Tasks.Task.Run(async () => {
    var log = new System.Text.StringBuilder();
    var token = await tokenTask;
    using (var http = new System.Net.Http.HttpClient()) {
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var cs = "https://services.api.unity.com/cloud-save/v1/data/projects/" + project + "/environments/" + env;
        foreach (var url in new[] { "https://services.api.unity.com/player-identity/v1/projects/" + project + "/users?limit=100&limit=100",
                                    cs + "/custom/usage/private/items?limit=100", cs + "/custom/usage/private/items", cs + "/custom/presence/private/items" }) {
            var r = await http.GetAsync(url);
            var b = await r.Content.ReadAsStringAsync();
            log.AppendLine(url.Replace(project, "<p>").Replace(env, "<e>") + " -> " + (int)r.StatusCode + " " + System.Text.RegularExpressions.Regex.Replace(b.Length > 300 ? b.Substring(0, 300) : b, "\"(id|key|name|username)\":\"[^\"]*\"", "\"$1\":\"…\""));
        }
    }
    System.IO.File.WriteAllText(outPath, log.ToString());
});
return "started";
