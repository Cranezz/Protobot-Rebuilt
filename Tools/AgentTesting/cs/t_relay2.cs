// (Play) Report routing check: on a throwaway guest (own profile; the editor's player is untouched) sends one test
// bug report (with a tiny zip) and one test appeal through Cloud Code "SendReport". Result in Temp/relay2.txt.
var outPath = System.IO.Path.GetFullPath("Temp/relay2.txt");
System.IO.File.WriteAllText(outPath, "working");
System.Func<System.Threading.Tasks.Task> run = async () => {
    var sb = new System.Text.StringBuilder();
    try {
        await Protobot.Online.OnlineServices.Start();
        var auth = Unity.Services.Authentication.AuthenticationService.Instance;
        var editorPlayer = auth.PlayerId;
        auth.SignOut();
        auth.SwitchProfile("trelay" + new System.Random().Next(1000, 9999));
        await auth.SignInAnonymouslyAsync();
        byte[] zip;
        using (var stream = new System.IO.MemoryStream()) {
            using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, true))
            using (var w = new System.IO.StreamWriter(archive.CreateEntry("report.md").Open())) w.Write("# Test\n\nRouting check.");
            zip = stream.ToArray();
        }
        var bug = await Protobot.Builds.BugReport.Send("bug", "**Test** · routing check: a bug report from Protobot lands here. (automated, delete me)", zip, "routing test.zip");
        sb.AppendLine("bug: " + (bug ?? "sent"));
        var appeal = await Protobot.Builds.BugReport.Send("appeal", "**Test** · routing check: an appeal from Protobot lands here. (automated, delete me)", null, null);
        sb.AppendLine("appeal: " + (appeal ?? "sent"));
        try { await auth.DeleteAccountAsync(); } catch (System.Exception e) { sb.AppendLine("cleanup: " + e.Message); }
        auth.SignOut(true);
        auth.SwitchProfile("default");
        await auth.SignInAnonymouslyAsync();
        sb.AppendLine("back to the editor's player: " + (auth.PlayerId == editorPlayer));
    }
    catch (System.Exception e) { sb.AppendLine("error " + e.GetType().Name + ": " + e.Message); }
    System.IO.File.WriteAllText(outPath, sb.ToString());
};
run();
return "started";
