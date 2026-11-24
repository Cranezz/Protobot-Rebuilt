var sb = new System.Text.StringBuilder();
foreach (var so in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.InstanceID))
    if (so.id.StartsWith("CCHL") || so.id.StartsWith("OMNI") || so.id.StartsWith("MOTR")) {
        var g = Protobot.ConnectedObjExtensions.GetConnectedObjects(so.gameObject, true, true);
        sb.AppendLine(so.id + " at " + so.transform.position.ToString("F1") + " group " + g.Count);
    }
return sb.ToString();
