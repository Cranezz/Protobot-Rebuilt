var sb = new System.Text.StringBuilder();
foreach (var so in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsSortMode.None)) sb.AppendLine(so.id + " state [" + so.state + "]");
return sb.ToString();
