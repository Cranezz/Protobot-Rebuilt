// Edit mode: deploys SubmitSkillsRun.js, GetStats.js and the programming skills leaderboard through the Deployment window's own providers.
// Result in Temp/deploy_result.txt.
UnityEditor.AssetDatabase.Refresh();
var outPath = System.IO.Path.GetFullPath("Temp/deploy_result.txt");
System.IO.File.WriteAllText(outPath, "working");
var sb = new System.Text.StringBuilder();
var jobs = new System.Collections.Generic.List<System.Threading.Tasks.Task>();
var deployed = new System.Collections.Generic.List<Unity.Services.DeploymentApi.Editor.IDeploymentItem>();
foreach (var provider in Unity.Services.DeploymentApi.Editor.Deployments.Instance.DeploymentProviders) {
    var items = new System.Collections.Generic.List<Unity.Services.DeploymentApi.Editor.IDeploymentItem>();
    foreach (var item in provider.DeploymentItems)
        if (item.Path != null && item.Path.Replace('\\', '/').Contains("Protobot/Cloud/") && (item.Path.EndsWith("SubmitSkillsRun.js") || item.Path.EndsWith("GetStats.js") || item.Path.EndsWith("programming-skills-override.lb"))) items.Add(item);
    sb.AppendLine(provider.Service + ": " + items.Count + " scripts");
    if (items.Count == 0) continue;
    deployed.AddRange(items);
    jobs.Add(provider.DeployCommand.ExecuteAsync(items));
}
System.Threading.Tasks.Task.WhenAll(jobs).ContinueWith(t => {
    if (t.Exception != null) sb.AppendLine("error " + t.Exception.GetBaseException().Message);
    foreach (var item in deployed) {
        sb.Append(item.Name + ": " + item.Status.Message + " " + item.Progress);
        foreach (var state in item.States) sb.Append(" | " + state.Description + " " + state.Detail);
        sb.AppendLine();
    }
    System.IO.File.WriteAllText(outPath, sb.ToString());
});
return "started";
