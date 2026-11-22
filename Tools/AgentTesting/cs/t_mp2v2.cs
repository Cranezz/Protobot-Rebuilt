// (Play, editor = host) Trailer 2v2. Temp/mp2v2.txt: "host" (open the demo robot, 13+ answer, host an invite-only 2v2
// without autonomous), "lobby" (red + ready), "players" (who's in, ready, robots), "start" (Start match), "setupready"
// (press Ready in match setup; illegal starts allowed for the test), "drive" (this robot loops round like -demodrive),
// "state" (the game's state, robots on the field), "end" (end the run now).
var cmd = System.IO.File.ReadAllText("Temp/mp2v2.txt").Trim();
var S = Protobot.Online.MatchSessions.Current;
if (cmd == "host") {
    UnityEngine.PlayerPrefs.SetString("V2.Birth", "2000-03"); UnityEngine.PlayerPrefs.SetInt("V2.TermsAgreed", Protobot.Online.AgeCheck.TermsVersion);
    var launch = UnityEngine.GameObject.Find("Launch Canvas");
    if (launch != null) foreach (var b in launch.GetComponentsInChildren<UnityEngine.UI.Button>(true)) if (b.name == "Close Button" && b.transform.parent.name == "Splash Screen" && b.gameObject.activeInHierarchy) { b.onClick.Invoke(); break; }
    Protobot.PartsManager.DestroyLoadedObjects();
    var path = @"C:\Dev\ProtobotRebuiltV2\ProtobotRebuiltV2\Sample Builds\Override Demo Robot.pbb";
    Protobot.Builds.SceneBuild.GenerateBuild(Protobot.Builds.BuildFile.Read(path));
    UnityEngine.Object.FindAnyObjectByType<Protobot.Builds.BuildsManager>().buildPath = path;
    var o = new Protobot.Online.MatchSessions.HostOptions { name = "Trailer match", mode = 0, maxPlayers = 4, inviteOnly = true, autonomous = false };
    Protobot.Online.MatchSessions.Host(o).ContinueWith(r => System.IO.File.WriteAllText("Temp/mp_host.txt", r.Result ?? "hosted"));
    return "hosting";
}
if (cmd == "code") return S == null ? "none" : S.Code;
if (cmd == "lobby") {
    Protobot.Online.OnlineWindow.Open();
    Protobot.Online.MatchSessions.Set(Protobot.Online.MatchSessions.TeamKey, "red");
    Protobot.Online.MatchSessions.Set(Protobot.Online.MatchSessions.RobotKey, Protobot.Online.MatchSessions.RobotName());
    Protobot.Online.MatchSessions.Set(Protobot.Online.MatchSessions.ReadyKey, "1");
    return "red, ready";
}
if (cmd == "players") {
    if (S == null) return "none";
    var sb = new System.Text.StringBuilder();
    foreach (var p in S.Players) sb.Append(Protobot.Online.MatchSessions.Get(p, Protobot.Online.MatchSessions.TeamKey) + "/" + Protobot.Online.MatchSessions.Get(p, Protobot.Online.MatchSessions.ReadyKey) + "/" + (Protobot.Online.MatchSessions.Get(p, Protobot.Online.MatchSessions.RobotKey) != "" ? "robot" : "-") + " ");
    return S.Players.Count + " players: " + sb;
}
if (cmd == "start") return Protobot.Online.OnlineMatch.HostStart() ?? "started";
if (cmd == "setupready") {
    Protobot.Online.OnlineMatch.TestAllowIllegal = true;
    var g = Protobot.Game.SkillsGame.Current;
    if (g == null || g.onlineReady == null) return "no game setup yet";
    g.onlineReady();
    return "ready in setup";
}
if (cmd == "drive") {
    var t0 = UnityEngine.Time.time;
    Protobot.Motion.Sandbox.TestSticks = () => {
        var t = UnityEngine.Time.time - t0;
        var backing = t % 7f > 6.1f;
        var forward = backing ? -0.6f : 0.7f * (0.8f + 0.2f * UnityEngine.Mathf.Sin(t * 0.37f));
        var turn = backing ? 0.5f : 0.5f * UnityEngine.Mathf.Sin(t * 0.6f);
        return (new UnityEngine.Vector2(0f, forward), new UnityEngine.Vector2(turn, 0f));
    };
    return "driving";
}
if (cmd == "state") {
    var g = Protobot.Game.SkillsGame.Current;
    int robots = 0;
    foreach (var rb in UnityEngine.Object.FindObjectsByType<UnityEngine.Rigidbody>(UnityEngine.FindObjectsSortMode.None)) if (rb.gameObject.layer == 16 && rb.mass > 0.5f) robots++;
    return g == null ? "no game" : "state " + g.state + ", robot bodies " + robots;
}
return "?";
