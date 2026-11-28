// (Play) Events (build 88). Temp/ev.txt holds the command:
//   "home off" | "create NAME [build] [in MIN]" | "open CODE" | "events" | "tab N" | "call ACTION CODE {json}" |
//   "out" (the last call's answer) | "state" | "shot NAME" | "end CODE" (ends then removes) | "forget"
// Calls run in the background: their answer goes to Temp/ev_out.txt ("working" until then).
var cmd = System.IO.File.ReadAllText("Temp/ev.txt").Trim();
var outPath = System.IO.Path.GetFullPath("Temp/ev_out.txt");
System.Action<System.Threading.Tasks.Task<Newtonsoft.Json.Linq.JObject>> Keep = task => {
    System.IO.File.WriteAllText(outPath, "working");
    task.ContinueWith(t => System.IO.File.WriteAllText(outPath, t.IsFaulted ? "fault " + t.Exception : t.Result.ToString(Newtonsoft.Json.Formatting.None)));
};
System.Func<string> State = () => {
    var s = "online " + Protobot.Online.OnlineServices.SignedIn + " ready " + Protobot.Online.AgeCheck.Ready + " me " + Protobot.Online.Events.Me
            + " window " + Protobot.Online.OnlineWindow.IsOpen + " joined " + string.Join(",", Protobot.Online.Events.Joined()) + " known " + string.Join(",", Protobot.Online.Events.Known.Keys)
            + " runner " + (Protobot.Online.EventsRunner.Instance != null) + " playing " + Protobot.Online.EventsRunner.PlayingMatch + " session " + (Protobot.Online.MatchSessions.Current != null ? Protobot.Online.MatchSessions.Current.Code : "-");
    foreach (var kv in Protobot.Online.Events.Known) s += "\n" + kv.Key + " " + kv.Value["state"] + " players " + ((Newtonsoft.Json.Linq.JArray)kv.Value["players"]).Count;
    return s;
};
if (cmd == "home off") {
    var launch = GameObject.Find("Launch Canvas");
    if (launch != null) foreach (var b in launch.GetComponentsInChildren<UnityEngine.UI.Button>(true)) if (b.name == "Close Button" && b.transform.parent.name == "Splash Screen" && b.gameObject.activeInHierarchy) { b.onClick.Invoke(); return "closed home"; }
    return "no home";
}
if (cmd == "state") return State();
if (cmd == "out") return System.IO.File.Exists(outPath) ? System.IO.File.ReadAllText(outPath) : "none";
if (cmd.StartsWith("shot ")) return Protobot.Editor.Playtest.Screenshot(cmd.Substring(5));
if (cmd == "events") {
    var screen = UnityEngine.Object.FindAnyObjectByType<Protobot.Online.OnlineScreen>();
    if (screen == null) { Protobot.Online.OnlineWindow.Open(); screen = UnityEngine.Object.FindAnyObjectByType<Protobot.Online.OnlineScreen>(); }
    screen.Show(Protobot.Online.OnlineScreen.Page.Events);
    return State();
}
if (cmd == "hostpage") {
    var screen = UnityEngine.Object.FindAnyObjectByType<Protobot.Online.OnlineScreen>();
    screen.Show(Protobot.Online.OnlineScreen.Page.EventHost);
    return "host page";
}
if (cmd.StartsWith("evtab ")) {
    var screen = UnityEngine.Object.FindAnyObjectByType<Protobot.Online.OnlineScreen>();
    typeof(Protobot.Online.OnlineScreen).GetField("eventsTab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(screen, int.Parse(cmd.Substring(6)));
    screen.Show(Protobot.Online.OnlineScreen.Page.Events);
    return "evtab";
}
if (cmd.StartsWith("tab ")) {
    var screen = UnityEngine.Object.FindAnyObjectByType<Protobot.Online.OnlineScreen>();
    typeof(Protobot.Online.OnlineScreen).GetField("eventTab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(screen, int.Parse(cmd.Substring(4)));
    screen.Show(Protobot.Online.OnlineScreen.Page.Event);
    return "tab";
}
if (cmd.StartsWith("open ")) { Protobot.Online.OnlineWindow.OpenEvent(cmd.Substring(5).Trim()); return State(); }
if (cmd == "forget") { UnityEngine.PlayerPrefs.DeleteKey(Protobot.Online.Events.JoinedKey); Protobot.Online.Events.Known.Clear(); return State(); }
if (cmd.StartsWith("create ")) {
    var words = cmd.Substring(7).Split(' ');
    var data = new System.Collections.Generic.Dictionary<string, object> { { "name", words[0].Replace('_', ' ') }, { "playerName", Protobot.Online.OnlineServices.Name }, { "play", true } };
    for (var i = 1; i < words.Length; i++) {
        if (words[i] == "build") { data["robots"] = "build"; data["buildMinutes"] = 15; }
        if (words[i] == "in") data["startIn"] = int.Parse(words[i + 1]) * 60;
        if (words[i] == "listed") data["listed"] = true;
    }
    var task = Protobot.Online.Events.Call("create", null, data);
    Keep(task);
    task.ContinueWith(t => { if (t.Result["code"] != null) System.IO.File.WriteAllText(System.IO.Path.GetFullPath("Temp/ev_code.txt"), (string)t.Result["code"]); });
    return "creating";
}
if (cmd.StartsWith("call ")) {
    var parts = cmd.Split(new[] { ' ' }, 4);
    var data = parts.Length > 3 ? Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, object>>(parts[3]) : null;
    Keep(Protobot.Online.Events.Call(parts[1], parts[2] == "-" ? null : parts[2], data));
    return "calling " + parts[1];
}
if (cmd.StartsWith("remember ")) { Protobot.Online.Events.Remember(cmd.Substring(9).Trim()); return State(); }
if (cmd == "demo") {
    var ev = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText("Temp/ev_demo.json"));
    Protobot.Online.Events.Known["DEMO01"] = ev;
    Protobot.Online.OnlineWindow.OpenEvent("DEMO01");
    return "demo";
}
if (cmd.StartsWith("openbuild ")) {
    var path = cmd.Substring(10).Trim();
    var manager = UnityEngine.Object.FindAnyObjectByType<Protobot.Builds.BuildsManager>();
    manager.AttemptLoad(Protobot.Builds.BuildsManager.ParsePath(path), path);
    return "opened " + path;
}
if (cmd.StartsWith("lockin ")) {
    var task = Protobot.Online.Events.LockIn(cmd.Substring(7).Trim());
    System.IO.File.WriteAllText(outPath, "working");
    task.ContinueWith(t => System.IO.File.WriteAllText(outPath, "lock: " + (t.Result ?? "ok")));
    return "locking";
}
if (cmd.StartsWith("mymatch ")) {
    var ev = Protobot.Online.Events.Known[cmd.Substring(8).Trim()];
    var m = Protobot.Online.Events.MyMatch(ev);
    return m == null ? "none; state " + ev["state"] : m.ToString(Newtonsoft.Json.Formatting.None);
}
if (cmd.StartsWith("join ")) {
    var w = cmd.Split(' ');
    Protobot.Online.EventsRunner.Instance.Join(w[1], w[2]);
    return State();
}
if (cmd == "ready") {
    Protobot.Online.OnlineMatch.TestAllowIllegal = true;
    var g = Protobot.Game.SkillsGame.Current;
    if (g == null || g.onlineReady == null) return "no setup yet";
    g.onlineReady();
    return "ready pressed";
}
if (cmd == "game") {
    var g = Protobot.Game.SkillsGame.Current;
    return g == null ? "no game; match " + Protobot.Online.OnlineMatch.Active : "game " + g.state + " running " + Protobot.Motion.Sandbox.Running + " match " + Protobot.Online.OnlineMatch.Active;
}
if (cmd == "exitgame") { var g = Protobot.Game.SkillsGame.Current; if (g != null) g.Exit(); return "exit"; }
return "unknown " + cmd;
