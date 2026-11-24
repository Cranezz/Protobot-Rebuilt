var sb = new System.Text.StringBuilder();
foreach (var so in UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
    if (so.id != null && so.id.StartsWith("CCHL-1x2-3")) sb.AppendLine((so.gameObject.activeSelf ? "on " : "off ") + so.transform.position.ToString("F2") + " fwd " + so.transform.forward.ToString("F2") + " up " + so.transform.up.ToString("F2") + " tag " + (Protobot.Builds.DemoPlayer.Target.TryGetValue(so.gameObject, out var t) ? t : "-"));
return sb.ToString();
