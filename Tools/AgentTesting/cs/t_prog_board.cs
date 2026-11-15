UnityEngine.PlayerPrefs.SetString("t_prog_board", "working");
System.Func<System.Threading.Tasks.Task> run = async () => {
    var prog = await Protobot.Online.Accounts.Leaderboard(50, true);
    var drive = await Protobot.Online.Accounts.Leaderboard(50, false);
    var replay = await Protobot.Online.Accounts.LoadReplay(Protobot.Online.OnlineServices.PlayerId, true);
    var stats = await Protobot.Online.Accounts.LoadStats();
    var s = "programming board: " + prog.top.Count + " runs, mine=" + (prog.mine == null ? "none" : prog.mine.score + " #" + prog.mine.rank) + " problem=" + prog.problem
        + " | driving mine=" + (drive.mine == null ? "none" : drive.mine.score.ToString())
        + " | replay=" + (replay == null ? "none" : replay.mode + " code " + (replay.build.code == null ? 0 : replay.build.code.Length) + " chars")
        + " | stats prog runs " + (stats == null ? -1 : stats.programmingRuns) + " best " + (stats == null ? -1 : stats.programmingBest) + " skills runs " + (stats == null ? -1 : stats.skillsRuns);
    UnityEngine.PlayerPrefs.SetString("t_prog_board", s);
};
run();
return "started";
