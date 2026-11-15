var sb = new System.Text.StringBuilder();
foreach (var provider in Unity.Services.DeploymentApi.Editor.Deployments.Instance.DeploymentProviders) {
    if (provider.GetType().Name != "CloudCodeDeploymentProvider") continue;
    foreach (var item in provider.DeploymentItems) {
        var t = item.GetType();
        foreach (var i in t.GetInterfaces()) if (i.Name == "IScript") {
            var n = i.GetProperty("Name").GetValue(item);
            sb.AppendLine(item.Path + " -> Name=" + (n == null ? "null" : n.ToString()) + " (" + (n == null ? "" : n.GetType().Name) + ")");
        }
    }
}
return sb.ToString();
