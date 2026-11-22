// Edit mode: deploys SendReport.js and DeleteMyData.js. First gives each script its name from its file (after a
// script reload the Cloud Code package can leave every name empty: then all are "duplicates" and none deploys).
// Result in Temp/deploy_result.txt.
UnityEditor.AssetDatabase.Refresh();
var outPath = System.IO.Path.GetFullPath("Temp/deploy_result.txt");
System.IO.File.WriteAllText(outPath, "working");
var sb = new System.Text.StringBuilder();
var jobs = new System.Collections.Generic.List<System.Threading.Tasks.Task>();
var deployed = new System.Collections.Generic.List<Unity.Services.DeploymentApi.Editor.IDeploymentItem>();
foreach (var provider in Unity.Services.DeploymentApi.Editor.Deployments.Instance.DeploymentProviders) {
    if (provider.GetType().Name != "CloudCodeDeploymentProvider") continue;
    var items = new System.Collections.Generic.List<Unity.Services.DeploymentApi.Editor.IDeploymentItem>();
    foreach (var item in provider.DeploymentItems) {
        if (item.Path == null || !(item.Path.EndsWith("SendReport.js") || item.Path.EndsWith("DeleteMyData.js"))) continue;
        System.Reflection.PropertyInfo nameProp = null;
        for (var t = item.GetType(); t != null && nameProp == null; t = t.BaseType)
            foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                if (p.Name == "Name" && p.PropertyType.Name == "ScriptName" && p.CanWrite) nameProp = p;
        if (nameProp != null) {
            var fromPath = nameProp.PropertyType.GetMethod("FromPath", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            nameProp.SetValue(item, fromPath.Invoke(null, new object[] { item.Path }));
            sb.AppendLine(item.Path + " named " + nameProp.GetValue(item));
        }
        items.Add(item);
    }
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
return sb.ToString();
