// Puts the report webhooks the Discord setup made (C:/Dev/ProtobotRebuiltV2/Discord/report_webhooks.json, private) on the
// server as Cloud Save private data: webhook_bug / webhook_report / webhook_appeal, and discordWebhook (fallback) = bug.
// Never prints them. Result in Temp/webhooks_set.txt.
var outPath = System.IO.Path.GetFullPath("Temp/webhooks_set.txt");
System.IO.File.WriteAllText(outPath, "working");
var json = System.IO.File.ReadAllText("C:/Dev/ProtobotRebuiltV2/Discord/report_webhooks.json");
var map = new System.Collections.Generic.Dictionary<string, string>();
foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(json, @"""(\w+)"":\s*""(https://[^""]+)"""))
    map[m.Groups[1].Value] = m.Groups[2].Value;
if (!map.ContainsKey("bug")) return "no bug webhook in the file (" + map.Count + " found)";
var type = System.Type.GetType("Protobot.EditorTools.LeaderboardModeration, Assembly-CSharp-Editor");
var set = type.GetMethod("SetWebhook", new[] { typeof(string), typeof(string) });
var jobs = new System.Collections.Generic.List<System.Threading.Tasks.Task<string>>();
var names = new System.Collections.Generic.List<string>();
foreach (var key in new System.Collections.Generic.List<string>(map.Keys)) {
    names.Add("webhook_" + key);
    jobs.Add((System.Threading.Tasks.Task<string>)set.Invoke(null, new object[] { map[key], "webhook_" + key }));
}
names.Add("discordWebhook");
jobs.Add((System.Threading.Tasks.Task<string>)set.Invoke(null, new object[] { map["bug"], "discordWebhook" }));
System.Threading.Tasks.Task.WhenAll(jobs.ToArray()).ContinueWith(t => {
    var sb = new System.Text.StringBuilder();
    for (int i = 0; i < jobs.Count; i++) sb.AppendLine(names[i] + ": " + (jobs[i].IsFaulted ? "error " + jobs[i].Exception.GetBaseException().Message : (jobs[i].Result ?? "saved")));
    System.IO.File.WriteAllText(outPath, sb.ToString());
});
return "started " + jobs.Count;
