// Play mode: signs in (guest), then posts a score straight to the leaderboard as that player, by both API paths (with
// and without the environment). Should be refused (403) by the access rule. Result + player id in Temp/direct_result.txt
// (run t_cleanup.cs with that id if a post got through).
var outPath = System.IO.Path.GetFullPath("Temp/direct_result.txt");
System.IO.File.WriteAllText(outPath, "working");
System.Func<System.Threading.Tasks.Task> run = async () => {
    var sb = new System.Text.StringBuilder();
    try {
        await Protobot.Online.OnlineServices.Start();
        var auth = Unity.Services.Authentication.AuthenticationService.Instance;
        var token = auth.AccessToken;
        var player = auth.PlayerId;
        var project = UnityEngine.Application.cloudProjectId;
        sb.AppendLine("player " + player);
        using (var http = new System.Net.Http.HttpClient()) {
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var urls = new[] {
                "https://leaderboards.services.api.unity.com/v1/projects/" + project + "/leaderboards/skills-override/scores/players/" + player,
            };
            foreach (var url in urls) {
                var response = await http.PostAsync(url, new System.Net.Http.StringContent("{\"score\":7}", System.Text.Encoding.UTF8, "application/json"));
                sb.AppendLine((int)response.StatusCode + " " + await response.Content.ReadAsStringAsync());
            }
        }
    }
    catch (System.Exception e) { sb.AppendLine("error " + e.Message); }
    try {
        var entry = await Unity.Services.Leaderboards.LeaderboardsService.Instance.AddPlayerScoreAsync("skills-override", 8);
        sb.AppendLine("sdk: accepted " + entry.Score);
    }
    catch (System.Exception e) { sb.AppendLine("sdk: refused " + e.Message); }
    System.IO.File.WriteAllText(outPath, sb.ToString());
};
run();
return "started";
