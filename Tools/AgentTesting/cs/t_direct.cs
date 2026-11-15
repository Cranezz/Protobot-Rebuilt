// Play mode: posts a score straight to the leaderboard as the signed-in test player (should be refused: 403).
var outPath = System.IO.Path.GetFullPath("Temp/direct_result.txt");
System.IO.File.WriteAllText(outPath, "working");
var token = Unity.Services.Authentication.AuthenticationService.Instance.AccessToken;
var player = Unity.Services.Authentication.AuthenticationService.Instance.PlayerId;
var project = UnityEngine.Application.cloudProjectId;
System.Threading.Tasks.Task.Run(async () => {
    try {
        using (var http = new System.Net.Http.HttpClient()) {
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var url = "https://leaderboards.services.api.unity.com/v1/projects/" + project + "/leaderboards/skills-override/scores/players/" + player;
            var response = await http.PostAsync(url, new System.Net.Http.StringContent("{\"score\":998}", System.Text.Encoding.UTF8, "application/json"));
            System.IO.File.WriteAllText(outPath, (int)response.StatusCode + " " + await response.Content.ReadAsStringAsync());
        }
    }
    catch (System.Exception e) { System.IO.File.WriteAllText(outPath, "error " + e.Message); }
});
return "started";
