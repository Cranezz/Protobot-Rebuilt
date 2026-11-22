// (Play) V2 privacy screens. Temp/privui.txt: "fresh" (forget the age answer and terms, open Multiplayer: the
// question shows), "kid" / "adult" (answer as under 13 / 13+, reopen Multiplayer), "page NAME" (Servers, Account,
// Host...), "host" (host an invite-only match: the lobby), "leave", "restore" (13+, agreed), "shot NAME".
var cmd = System.IO.File.ReadAllText("Temp/privui.txt").Trim();
var t = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
if (cmd.StartsWith("shot ")) return (string)t.GetMethod("Screenshot").Invoke(null, new object[] { cmd.Substring(5) });
var launch = UnityEngine.GameObject.Find("Launch Canvas");
if (launch != null) foreach (var b in launch.GetComponentsInChildren<UnityEngine.UI.Button>(true)) if (b.name == "Close Button" && b.transform.parent.name == "Splash Screen" && b.gameObject.activeInHierarchy) { b.onClick.Invoke(); break; }
if (cmd == "open") { Protobot.Online.OnlineWindow.Open(); return "opened"; }
if (cmd == "fresh") {
    UnityEngine.PlayerPrefs.DeleteKey("V2.Birth"); UnityEngine.PlayerPrefs.DeleteKey("V2.TermsAgreed");
    Protobot.Online.OnlineWindow.Close();
    Protobot.Online.OnlineWindow.Open();
    return "fresh: ready " + Protobot.Online.AgeCheck.Ready;
}
if (cmd == "kid" || cmd == "adult" || cmd == "restore") {
    Protobot.Interface.Dialog.CloseAll();
    UnityEngine.PlayerPrefs.SetString("V2.Birth", cmd == "kid" ? "2016-03" : "2000-03"); UnityEngine.PlayerPrefs.SetInt("V2.TermsAgreed", Protobot.Online.AgeCheck.TermsVersion);
    Protobot.Online.OnlineWindow.Close();
    return cmd + ": under 13 " + Protobot.Online.AgeCheck.Under13 + ", name " + Protobot.Online.OnlineServices.Name + ", voice can hear " + Protobot.Online.Voice.CanHear;
}
var screen = UnityEngine.Object.FindAnyObjectByType<Protobot.Online.OnlineScreen>();
if (cmd.StartsWith("page ")) {
    if (screen == null) return "no screen";
    screen.Show((Protobot.Online.OnlineScreen.Page)System.Enum.Parse(typeof(Protobot.Online.OnlineScreen.Page), cmd.Substring(5)));
    return "page";
}
if (cmd == "host") {
    var o = new Protobot.Online.MatchSessions.HostOptions { name = "Test match", mode = 0, maxPlayers = 4, inviteOnly = true };
    Protobot.Online.MatchSessions.Host(o).ContinueWith(r => System.IO.File.WriteAllText("Temp/privui_host.txt", r.Result ?? "hosted"));
    return "hosting";
}
if (cmd == "leave") { Protobot.Online.MatchSessions.Leave(null); return "leaving"; }
return "?";
