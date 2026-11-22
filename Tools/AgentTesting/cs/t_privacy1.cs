// Play mode (V2 privacy batch): on a throwaway guest (its own Authentication profile, so the editor's test player is
// untouched) sends one test report through Cloud Code "SendReport" (a small zip: it lands in the Discord channel marked
// as a test), checks the rate limit, keeps a public item and a file like a replay and a report would, then runs
// "Delete my online data" and checks they're gone and the player is on the hidden and deletion lists. Back to the
// default profile after. Result in Temp/privacy1.txt.
var outPath = System.IO.Path.GetFullPath("Temp/privacy1.txt");
System.IO.File.WriteAllText(outPath, "working");
System.Func<System.Threading.Tasks.Task> run = async () => {
    var sb = new System.Text.StringBuilder();
    System.Action Mark = () => System.IO.File.WriteAllText(outPath, "working\n" + sb);
    try {
        sb.AppendLine("starting");
        Mark();
        await Protobot.Online.OnlineServices.Start();
        sb.AppendLine("started services: " + Protobot.Online.OnlineServices.SignedIn + " " + Protobot.Online.OnlineServices.Problem);
        Mark();
        var auth = Unity.Services.Authentication.AuthenticationService.Instance;
        var save = Unity.Services.CloudSave.CloudSaveService.Instance;
        var editorPlayer = auth.PlayerId;
        auth.SignOut();
        auth.SwitchProfile("tpriv" + new System.Random().Next(1000, 9999));
        await auth.SignInAnonymouslyAsync();
        var me = auth.PlayerId;
        Mark(); sb.AppendLine("throwaway guest " + (me != editorPlayer ? "ok" : "SAME AS EDITOR PLAYER"));

        // A small zip, like a report.
        byte[] zip;
        using (var stream = new System.IO.MemoryStream()) {
            using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, true)) {
                using (var w = new System.IO.StreamWriter(archive.CreateEntry("report.md").Open()))
                    w.Write("# Test\n\nAutomated check of the report relay (no action needed). Log line with a path: "
                            + Protobot.Builds.BugReport.Scrub(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile) + "\\AppData\\x.log"));
            }
            zip = stream.ToArray();
        }
        var sent = await Protobot.Builds.BugReport.Send("test", "**Test** · automated check of the new report relay (Protobot privacy update). No action needed.", zip, "relay test.zip");
        Mark(); sb.AppendLine("send 1: " + (sent ?? "sent"));
        var two = await Protobot.Builds.BugReport.Send("test", "**Test** · rate limit check 2 (text only)", null, null);
        Mark(); sb.AppendLine("send 2: " + (two ?? "sent"));
        var three = await Protobot.Builds.BugReport.Send("test", "**Test** · rate limit check 3 (text only)", null, null);
        Mark(); sb.AppendLine("send 3: " + (three ?? "sent"));
        var four = await Protobot.Builds.BugReport.Send("test", "**Test** · should be refused", null, null);
        Mark(); sb.AppendLine("send 4 (must be refused): " + (four ?? "SENT"));
        var files = await save.Files.Player.ListAllAsync();
        Mark(); sb.AppendLine("report files left after sending: " + files.Count);

        // Things deletion must remove: a public item (like a shared replay) and a file.
        await save.Data.Player.SaveAsync(new System.Collections.Generic.Dictionary<string, object> { { "skills-replay", "test" } },
            new Unity.Services.CloudSave.Models.Data.Player.SaveOptions(new Unity.Services.CloudSave.Models.Data.Player.PublicWriteAccessClassOptions()));
        await save.Files.Player.SaveAsync("report-test", new byte[] { 1, 2, 3 });
        Mark(); sb.AppendLine("saved a public item and a file");

        var problem = await Protobot.Online.Accounts.DeleteEverything();
        Mark(); sb.AppendLine("delete: " + (problem ?? "ok") + ", now a new guest: " + (auth.PlayerId != me));

        // Read the lists back as the new guest (the hidden list is readable by players).
        var hidden = await save.Data.Custom.LoadAsync("leaderboard", new System.Collections.Generic.HashSet<string> { "hidden" });
        System.Collections.Generic.List<string> list = null;
        if (hidden.ContainsKey("hidden")) list = hidden["hidden"].Value.GetAs<System.Collections.Generic.List<string>>();
        Mark(); sb.AppendLine("on the hidden list: " + (list != null && list.Contains(me)));
        System.IO.File.WriteAllText(System.IO.Path.GetFullPath("Temp/privacy1_id.txt"), me);

        auth.SignOut(true);
        auth.SwitchProfile("default");
        await auth.SignInAnonymouslyAsync();
        Mark(); sb.AppendLine("back to the editor's player: " + (auth.PlayerId == editorPlayer));
    }
    catch (System.Exception e) { sb.AppendLine("error " + e.GetType().Name + ": " + e.Message); }
    System.IO.File.WriteAllText(outPath, sb.ToString());
};
run();
return "started";
