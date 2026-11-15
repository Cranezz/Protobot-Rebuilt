// Play mode: creates a throwaway account (random name and password, never written out), signs out and back in,
// then deletes it. Result in Temp/account_result.txt.
var outPath = System.IO.Path.GetFullPath("Temp/account_result.txt");
System.IO.File.WriteAllText(outPath, "working");
System.Func<System.Threading.Tasks.Task> run = async () => {
    var sb = new System.Text.StringBuilder();
    var auth = Unity.Services.Authentication.AuthenticationService.Instance;
    try {
        var rnd = new System.Random();
        var user = "pbtest" + rnd.Next(100000, 999999);
        var pass = "Pb!" + System.Guid.NewGuid().ToString("N").Substring(0, 10) + "x7Q";
        await Protobot.Online.OnlineServices.Start();
        var guest = auth.PlayerId;
        var made = await Protobot.Online.Accounts.CreateAccount(user, pass);
        sb.AppendLine("create: " + (made ?? "ok") + ", same player as the guest: " + (auth.PlayerId == guest) + ", username " + Protobot.Online.Accounts.Username);
        var taken = await Protobot.Online.Accounts.CreateAccount(user, pass);
        sb.AppendLine("create again (already has one): " + (taken ?? "ok"));
        await Protobot.Online.Accounts.SignOut();
        sb.AppendLine("signed out, guest now: " + Protobot.Online.Accounts.IsGuest);
        var wrong = await Protobot.Online.Accounts.SignIn(user, pass + "no");
        sb.AppendLine("wrong password: " + (wrong ?? "ok"));
        var back = await Protobot.Online.Accounts.SignIn(user, pass);
        sb.AppendLine("sign in: " + (back ?? "ok") + ", same player: " + (auth.PlayerId == guest));
        await auth.DeleteAccountAsync();
        sb.AppendLine("test account deleted");
        await auth.SignInAnonymouslyAsync();
    }
    catch (System.Exception e) { sb.AppendLine("error " + e.Message); }
    System.IO.File.WriteAllText(outPath, sb.ToString());
};
run();
return "started";
