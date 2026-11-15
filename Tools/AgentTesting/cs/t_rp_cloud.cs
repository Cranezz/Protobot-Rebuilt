// Play mode: shares the last replay as this guest's public data, reads it back through Accounts.LoadReplay. Temp/rpcloud.txt
var outPath = System.IO.Path.GetFullPath("Temp/rpcloud.txt");
System.IO.File.WriteAllText(outPath, "working");
System.Func<System.Threading.Tasks.Task> run = async () => {
    var sb = new System.Text.StringBuilder();
    try {
        await Protobot.Online.OnlineServices.Start();
        var text = System.Convert.ToBase64String(Protobot.Game.Replays.ToBytes(Protobot.Game.Replays.Last));
        await Unity.Services.CloudSave.CloudSaveService.Instance.Data.Player.SaveAsync(new System.Collections.Generic.Dictionary<string, object> { { "skills-replay", text } },
            new Unity.Services.CloudSave.Models.Data.Player.SaveOptions(new Unity.Services.CloudSave.Models.Data.Player.PublicWriteAccessClassOptions()));
        sb.AppendLine("saved " + text.Length + " chars as " + Protobot.Online.OnlineServices.PlayerId);
        var back = await Protobot.Online.Accounts.LoadReplay(Protobot.Online.OnlineServices.PlayerId);
        sb.AppendLine("read back: " + (back != null ? back.Title + ", " + back.frames + " frames" : "null"));
    }
    catch (System.Exception e) { sb.AppendLine("error " + e.Message); }
    System.IO.File.WriteAllText(outPath, sb.ToString());
};
run();
return "started";
