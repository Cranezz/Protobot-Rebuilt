var project = UnityEditor.CloudProjectSettings.projectId;
var env = Unity.Services.Core.Editor.Environments.EnvironmentsApi.Instance.ActiveEnvironmentId;
var outPath = System.IO.Path.GetFullPath("Temp/privacy4.txt");
System.IO.File.WriteAllText(outPath, "");
var tokenTask = new Unity.Services.Core.Editor.AccessTokens().GetServicesGatewayTokenAsync();
System.Threading.Tasks.Task.Run(async () => {
    var token = await tokenTask;
    var data = "https://services.api.unity.com/cloud-save/v1/data/projects/" + project + "/environments/" + env;
    using (var http = new System.Net.Http.HttpClient()) {
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var queue = await (await http.GetAsync(data + "/custom/moderation/private/items?keys=deletions")).Content.ReadAsStringAsync();
        var hidden = await (await http.GetAsync(data + "/custom/leaderboard/items?keys=hidden")).Content.ReadAsStringAsync();
        System.IO.File.WriteAllText(outPath, "deletion list has it " + queue.Contains("dSyQgrCAVKuTZKSwYQLY8HttfzHO") + ", hidden list has it " + hidden.Contains("dSyQgrCAVKuTZKSwYQLY8HttfzHO"));
    }
});
return "started";
