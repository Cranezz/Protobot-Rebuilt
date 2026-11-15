// Play mode: exercises the cloud side and writes what happened to Temp/cloud_result.txt.
var outPath = System.IO.Path.GetFullPath("Temp/cloud_result.txt");
System.IO.File.WriteAllText(outPath, "working");
System.Func<System.Threading.Tasks.Task> run = async () => {
    var sb = new System.Text.StringBuilder();
    try {
        var ok = await Protobot.Online.OnlineServices.Start();
        sb.AppendLine("signed in " + ok + " " + Protobot.Online.OnlineServices.PlayerId);
        var stats = await Protobot.Online.Accounts.LoadStats();
        sb.AppendLine("stats " + (stats != null ? "runs " + stats.skillsRuns + " best " + stats.skillsBest : "null"));
        var items = new System.Collections.Generic.List<(string, int)> { ("Yellow pin halves", 10), ("Red pin halves", 5) };
        var msg = await Protobot.Online.Accounts.SubmitSkills(15, items, new UnityEngine.Vector3(17.5f, 17.9f, 14f), 60f, "cloud test");
        sb.AppendLine("good run: " + (msg ?? "(no message)"));
        var bad = await Protobot.Online.Accounts.SubmitSkills(500, items, new UnityEngine.Vector3(17.5f, 17.9f, 14f), 60f, "cloud test");
        sb.AppendLine("bad run: " + (bad ?? "(no message)"));
        var big = await Protobot.Online.Accounts.SubmitSkills(15, items, new UnityEngine.Vector3(19f, 17.9f, 14f), 60f, "cloud test");
        sb.AppendLine("oversized run: " + (big ?? "(no message)"));
        var board = await Protobot.Online.Accounts.Leaderboard();
        sb.AppendLine("leaderboard " + board.Item1.Count + " entries, problem " + board.Item3 + ", mine " + (board.Item2 != null ? board.Item2.score + " #" + board.Item2.rank : "none"));
        try {
            await Unity.Services.Leaderboards.LeaderboardsService.Instance.AddPlayerScoreAsync("skills-override", 999);
            sb.AppendLine("DIRECT POST ALLOWED (bad)");
        }
        catch (System.Exception e) { sb.AppendLine("direct post refused: " + e.Message); }
        stats = await Protobot.Online.Accounts.LoadStats();
        sb.AppendLine("stats after " + (stats != null ? "runs " + stats.skillsRuns + " best " + stats.skillsBest : "null"));
    }
    catch (System.Exception e) { sb.AppendLine("error " + e); }
    System.IO.File.WriteAllText(outPath, sb.ToString());
};
run();
return "started";
