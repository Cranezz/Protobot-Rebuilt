// Edit or Play mode: deletes every item of Cloud Save custom data "events" (test events, the index, the champions list)
// through the admin API with the editor's sign-in. Only for clearing test data before events go live. Result in
// Temp/ev_cleanup.txt (never the token).
var project = UnityEditor.CloudProjectSettings.projectId;
var env = Unity.Services.Core.Editor.Environments.EnvironmentsApi.Instance.ActiveEnvironmentId;
var outPath = System.IO.Path.GetFullPath("Temp/ev_cleanup.txt");
System.IO.File.WriteAllText(outPath, "working");
var tokenTask = new Unity.Services.Core.Editor.AccessTokens().GetServicesGatewayTokenAsync();
System.Threading.Tasks.Task.Run(async () => {
    var log = new System.Text.StringBuilder();
    try {
        var token = await tokenTask;
        var baseUrl = "https://services.api.unity.com/cloud-save/v1/data/projects/" + project + "/environments/" + env + "/custom/events/items";
        using (var http = new System.Net.Http.HttpClient()) {
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var list = await http.GetAsync(baseUrl);
            var body = await list.Content.ReadAsStringAsync();
            log.AppendLine("list " + (int)list.StatusCode);
            var keys = new System.Collections.Generic.List<string>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(body, "\"key\":\"([A-Za-z0-9_-]+)\"")) keys.Add(m.Groups[1].Value);
            foreach (var k in keys) {
                var r = await http.DeleteAsync(baseUrl + "/" + k);
                log.AppendLine("delete " + k + " " + (int)r.StatusCode);
            }
        }
    }
    catch (System.Exception e) { log.AppendLine("error " + e.Message); }
    System.IO.File.WriteAllText(outPath, log.ToString());
});
return "started";
