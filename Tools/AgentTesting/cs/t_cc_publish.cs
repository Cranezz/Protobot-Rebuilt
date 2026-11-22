// Edit mode: creates (or updates) and publishes Cloud Code scripts through the admin API with the editor's sign-in:
// the names in PlayerPrefs "t_cc_names" (comma list; files Assets/Protobot/Cloud/<name>.js, parameters from its .meta).
// Shows the server's own answer (the Deployment window only says "status 0"). Result (never the token) in
// Temp/cc_publish.txt.
var project = UnityEditor.CloudProjectSettings.projectId;
var env = Unity.Services.Core.Editor.Environments.EnvironmentsApi.Instance.ActiveEnvironmentId;
var outPath = System.IO.Path.GetFullPath("Temp/cc_publish.txt");
System.IO.File.WriteAllText(outPath, "working");
var names = UnityEngine.PlayerPrefs.GetString("t_cc_names", "SendReport,DeleteMyData").Split(',');
var jobs = new System.Collections.Generic.List<string[]>();
foreach (var raw in names) {
    var name = raw.Trim();
    var code = System.IO.File.ReadAllText("Assets/Protobot/Cloud/" + name + ".js");
    var meta = System.IO.File.ReadAllLines("Assets/Protobot/Cloud/" + name + ".js.meta");
    var ps = new System.Collections.Generic.List<string>();
    string pName = null; int pType = 0;
    string[] typeNames = { "String", "NUMERIC", "BOOLEAN", "JSON", "ANY" };
    foreach (var line in meta) {
        var t = line.Trim();
        if (t.StartsWith("- Name: ")) pName = t.Substring(8);
        else if (t.StartsWith("ParameterType: ")) pType = int.Parse(t.Substring(15));
        else if (t.StartsWith("Required: ") && pName != null) {
            ps.Add("{\"name\":\"" + pName + "\",\"type\":\"" + typeNames[pType].ToUpperInvariant() + "\",\"required\":" + (t.EndsWith("1") ? "true" : "false") + "}");
            pName = null;
        }
    }
    jobs.Add(new[] { name, code, "[" + string.Join(",", ps) + "]" });
}
System.Func<string, string> Json = s => {
    var sb = new System.Text.StringBuilder("\"");
    foreach (var c in s) {
        if (c == '"' || c == '\\') sb.Append('\\').Append(c);
        else if (c == '\n') sb.Append("\\n");
        else if (c == '\r') sb.Append("\\r");
        else if (c == '\t') sb.Append("\\t");
        else if (c < 32) sb.Append(' ');
        else sb.Append(c);
    }
    return sb.Append('"').ToString();
};
var bodies = new System.Collections.Generic.List<string[]>();
foreach (var j in jobs)
    bodies.Add(new[] { j[0], "{\"name\":\"" + j[0] + "\",\"type\":\"API\",\"language\":\"JS\",\"code\":" + Json(j[1]) + ",\"params\":" + j[2] + "}",
                "{\"code\":" + Json(j[1]) + ",\"params\":" + j[2] + "}" });
var tokenTask = new Unity.Services.Core.Editor.AccessTokens().GetServicesGatewayTokenAsync();
System.Threading.Tasks.Task.Run(async () => {
    var log = new System.Text.StringBuilder();
    try {
        var token = await tokenTask;
        var baseUrl = "https://services.api.unity.com/cloud-code/v1/projects/" + project + "/environments/" + env + "/scripts";
        using (var http = new System.Net.Http.HttpClient()) {
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            foreach (var b in bodies) {
                var exists = await http.GetAsync(baseUrl + "/" + b[0]);
                System.Net.Http.HttpResponseMessage r;
                if ((int)exists.StatusCode == 404)
                    r = await http.PostAsync(baseUrl, new System.Net.Http.StringContent(b[1], System.Text.Encoding.UTF8, "application/json"));
                else
                    r = await http.SendAsync(new System.Net.Http.HttpRequestMessage(new System.Net.Http.HttpMethod("PATCH"), baseUrl + "/" + b[0]) { Content = new System.Net.Http.StringContent(b[2], System.Text.Encoding.UTF8, "application/json") });
                log.AppendLine(b[0] + " " + ((int)exists.StatusCode == 404 ? "create " : "update ") + (int)r.StatusCode + " " + await r.Content.ReadAsStringAsync());
                var p = await http.PostAsync(baseUrl + "/" + b[0] + "/publish", new System.Net.Http.StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
                log.AppendLine(b[0] + " publish " + (int)p.StatusCode + " " + await p.Content.ReadAsStringAsync());
            }
        }
    }
    catch (System.Exception e) { log.AppendLine("error " + e.GetType().Name + ": " + e.Message); }
    System.IO.File.WriteAllText(outPath, log.ToString());
});
return "started " + string.Join(",", names);
