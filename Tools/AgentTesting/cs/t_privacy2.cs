// Edit or Play mode (V2 privacy batch): for the player t_privacy1 deleted (Temp/privacy1_id.txt), checks through the
// admin API that its public items and files are gone and that it's on the moderators' deletion list, then runs the
// moderation window's Finish deletions and checks both lists no longer have it. Result in Temp/privacy2.txt.
var outPath = System.IO.Path.GetFullPath("Temp/privacy2.txt");
System.IO.File.WriteAllText(outPath, "working");
var id = System.IO.File.ReadAllText(System.IO.Path.GetFullPath("Temp/privacy1_id.txt")).Trim();
var project = UnityEditor.CloudProjectSettings.projectId;
var env = Unity.Services.Core.Editor.Environments.EnvironmentsApi.Instance.ActiveEnvironmentId;
var type = System.Type.GetType("Protobot.EditorTools.LeaderboardModeration, Assembly-CSharp-Editor");
var tokenTask = new Unity.Services.Core.Editor.AccessTokens().GetServicesGatewayTokenAsync();
var window = UnityEngine.ScriptableObject.CreateInstance(type);
System.Threading.Tasks.Task.Run(async () => {
    var sb = new System.Text.StringBuilder();
    try {
        var token = await tokenTask;
        var data = "https://services.api.unity.com/cloud-save/v1/data/projects/" + project + "/environments/" + env;
        using (var http = new System.Net.Http.HttpClient()) {
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var pub = await http.GetAsync(data + "/players/" + id + "/public/items");
            sb.AppendLine("public items: " + (int)pub.StatusCode + " " + (await pub.Content.ReadAsStringAsync()).Replace(id, "<id>"));
            var prot = await http.GetAsync(data + "/players/" + id + "/protected/items");
            sb.AppendLine("protected items: " + (int)prot.StatusCode + " " + (await prot.Content.ReadAsStringAsync()).Replace(id, "<id>"));
            var files = await http.GetAsync("https://services.api.unity.com/cloud-save/v1/files/projects/" + project + "/environments/" + env + "/players/" + id + "/files");
            sb.AppendLine("files: " + (int)files.StatusCode + " " + (await files.Content.ReadAsStringAsync()).Replace(id, "<id>"));
            var queue = await http.GetAsync(data + "/custom/moderation/private/items?keys=deletions");
            var queueText = await queue.Content.ReadAsStringAsync();
            sb.AppendLine("on the deletion list: " + queueText.Contains(id));
        }
    }
    catch (System.Exception e) { sb.AppendLine("error " + e.Message); }
    System.IO.File.WriteAllText(outPath, "working\n" + sb);
    UnityEditor.EditorApplication.delayCall += () => {
        type.GetMethod("FinishDeletions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(window, null);
    };
    await System.Threading.Tasks.Task.Delay(15000);
    try {
        var token = await new Unity.Services.Core.Editor.AccessTokens().GetServicesGatewayTokenAsync();
        var data = "https://services.api.unity.com/cloud-save/v1/data/projects/" + project + "/environments/" + env;
        using (var http = new System.Net.Http.HttpClient()) {
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var queue = await (await http.GetAsync(data + "/custom/moderation/private/items?keys=deletions")).Content.ReadAsStringAsync();
            var hidden = await (await http.GetAsync(data + "/custom/leaderboard/items?keys=hidden")).Content.ReadAsStringAsync();
            sb.AppendLine("after Finish deletions: on the deletion list " + queue.Contains(id) + ", on the hidden list " + hidden.Contains(id));
            sb.AppendLine("window status: " + type.GetField("status", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(window));
        }
    }
    catch (System.Exception e) { sb.AppendLine("error " + e.Message); }
    System.IO.File.WriteAllText(outPath, sb.ToString());
});
return "started";
