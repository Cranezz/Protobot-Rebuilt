// (Play) Lobby robot checks. Temp/mp.txt: "host" (13+ answer set for this editor, host an invite-only test match, open
// the lobby), "empty" (clear the build), "load PATH" (open a build file as the robot), "state" (my robot shown in the
// lobby, ready, what Ready would say), "ready" (press Ready the way the button does), "back" (Back to menu), "shot NAME".
var cmd = System.IO.File.ReadAllText("Temp/mp.txt").Trim();
var S = Protobot.Online.MatchSessions.Current;
if (cmd == "host") {
    UnityEngine.PlayerPrefs.SetString("V2.Birth", "2000-03"); UnityEngine.PlayerPrefs.SetInt("V2.TermsAgreed", Protobot.Online.AgeCheck.TermsVersion);
    var launch = UnityEngine.GameObject.Find("Launch Canvas");
    if (launch != null) foreach (var b in launch.GetComponentsInChildren<UnityEngine.UI.Button>(true)) if (b.name == "Close Button" && b.transform.parent.name == "Splash Screen" && b.gameObject.activeInHierarchy) { b.onClick.Invoke(); break; }
    var o = new Protobot.Online.MatchSessions.HostOptions { name = "Agent test", mode = 0, maxPlayers = 4, inviteOnly = true };
    var t = Protobot.Online.MatchSessions.Host(o);
    t.ContinueWith(r => System.IO.File.WriteAllText("Temp/mp_host.txt", r.Result ?? "hosted"));
    return "hosting";
}
if (cmd == "open") { Protobot.Online.OnlineWindow.Open(); return "lobby open"; }
if (cmd == "empty") { Protobot.PartsManager.DestroyLoadedObjects(); return "build cleared"; }
if (cmd.StartsWith("load ")) {
    Protobot.PartsManager.DestroyLoadedObjects();
    var path = cmd.Substring(5);
    var data = Protobot.Builds.BuildFile.Read(path);
    Protobot.Builds.SceneBuild.GenerateBuild(data);
    var manager = UnityEngine.Object.FindAnyObjectByType<Protobot.Builds.BuildsManager>();
    manager.buildPath = path;
    return "loaded " + data.parts.Length + " parts";
}
if (cmd == "state") {
    if (S == null) return "not in a match";
    var me = S.CurrentPlayer;
    return "robot shown '" + Protobot.Online.MatchSessions.Get(me, Protobot.Online.MatchSessions.RobotKey) + "', ready " + Protobot.Online.MatchSessions.Get(me, Protobot.Online.MatchSessions.ReadyKey)
           + ", robot now '" + Protobot.Online.MatchSessions.RobotName() + "', problem: " + (Protobot.Game.SkillsGame.RobotProblem() ?? "none");
}
if (cmd == "ready") {
    if (S == null) return "not in a match";
    Protobot.Online.MatchSessions.Set(Protobot.Online.MatchSessions.TeamKey, "red");
    var screen = UnityEngine.Object.FindAnyObjectByType<Protobot.Online.OnlineScreen>();
    var ready = screen.GetComponentsInChildren<UnityEngine.UI.Button>(true).FirstOrDefault(b => b.name == "Ready");
    if (ready == null) return "no Ready button";
    ready.onClick.Invoke();
    return "pressed Ready";
}
if (cmd == "back") {
    var screen = UnityEngine.Object.FindAnyObjectByType<Protobot.Online.OnlineScreen>();
    if (screen == null) return "no screen";
    screen.BackToMenu();
    return "back to menu";
}
if (cmd.StartsWith("shot ")) {
    var t = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
    return (string)t.GetMethod("Screenshot").Invoke(null, new object[] { cmd.Substring(5) });
}
return "?";
