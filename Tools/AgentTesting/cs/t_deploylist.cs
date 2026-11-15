var sb = new System.Text.StringBuilder();
foreach (var provider in Unity.Services.DeploymentApi.Editor.Deployments.Instance.DeploymentProviders) {
    sb.AppendLine(provider.Service + " (" + provider.GetType().Name + "):");
    foreach (var item in provider.DeploymentItems) sb.AppendLine("  " + item.Name + " | " + item.Path + " | " + item.GetType().Name);
}
return sb.ToString();
