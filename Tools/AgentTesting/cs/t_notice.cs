// Play mode: Temp/notice.txt = "notify TEXT" | "ask TEXT" | "update" | "updatemac" (shows a card as the app would).
var cmd = System.IO.File.ReadAllText("Temp/notice.txt");
var i = cmd.IndexOf(' ');
var kind = i < 0 ? cmd : cmd.Substring(0, i);
var text = i < 0 ? "" : cmd.Substring(i + 1);
if (kind == "update")
    Protobot.Online.Notices.ShowUpdate("2.0.0-beta.1.85", "Protobot v2.0 Beta 1 (build 85) is out (1.2 MB). Protobot saves your build, updates and restarts.",
        "Save and update now", null, "What's new", null);
else if (kind == "updatemac")
    Protobot.Online.Notices.ShowUpdate("2.0.0-beta.1.85", "Protobot v2.0 Beta 1 (build 85) is out. Download the new Mac version from the releases page.", "Download", null, null, null);
else Protobot.Online.Notices.Message("t" + System.DateTime.Now.Ticks, kind, text);
return "queued " + kind;
