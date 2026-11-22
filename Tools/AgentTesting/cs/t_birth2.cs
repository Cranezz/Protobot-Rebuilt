// (Play) V2 birthdays, part 2 (after t_birth1, on its throwaway guest; this computer answered 13+): makes an account
// (random name and password, never written out) with a 10-year-old's birthday: it's kept on the account, the account's
// younger answer wins (no voice, made-up name), it can't be changed, it comes back on signing in, and an email-like
// username is refused. Then deletes the test player and goes back to the editor's own player. Temp/birth2.txt.
var outPath = System.IO.Path.GetFullPath("Temp/birth2.txt");
System.IO.File.WriteAllText(outPath, "working");
var editorPlayer = System.IO.File.ReadAllText(System.IO.Path.GetFullPath("Temp/birth_editor.txt")).Trim();
System.Func<System.Threading.Tasks.Task> run = async () => {
    var sb = new System.Text.StringBuilder();
    System.Action Mark = () => System.IO.File.WriteAllText(outPath, "working\n" + sb);
    try {
        var auth = Unity.Services.Authentication.AuthenticationService.Instance;
        var A = typeof(Protobot.Online.Accounts);
        var rnd = new System.Random();
        var user = "pbtest" + rnd.Next(100000, 999999);
        var pass = "Pb!" + System.Guid.NewGuid().ToString("N").Substring(0, 10) + "x7Q";
        var kidYear = (System.DateTime.Now.Year - 10).ToString();
        sb.AppendLine("before: under 13 " + Protobot.Online.AgeCheck.Under13 + ", voice " + Protobot.Online.Voice.CanHear);
        var email = await Protobot.Online.Accounts.CreateAccount("kid@mail.com", pass, 2, kidYear);
        sb.AppendLine("email username: " + (email ?? "ACCEPTED"));
        var noBirth = await Protobot.Online.Accounts.CreateAccount(user, pass, -1, "");
        sb.AppendLine("no birthday: " + (noBirth ?? "ACCEPTED"));
        Mark();
        var made = await Protobot.Online.Accounts.CreateAccount(user, pass, 2, kidYear);
        sb.AppendLine("create: " + (made ?? "ok") + ", birth " + Protobot.Online.Accounts.Birth + ", under 13 " + Protobot.Online.AgeCheck.Under13
                      + ", voice " + Protobot.Online.Voice.CanHear + ", name made up " + (Protobot.Online.OnlineServices.Name == Protobot.Online.OnlineServices.KidName));
        var change = await Protobot.Online.Accounts.SaveBirth(2, "1990");
        sb.AppendLine("change to 1990: birth still " + Protobot.Online.Accounts.Birth + " (" + (change ?? "no problem reported") + ")");
        Mark();
        await Protobot.Online.Accounts.SignOut();
        sb.AppendLine("signed out: birth " + (Protobot.Online.Accounts.Birth ?? "null") + ", under 13 " + Protobot.Online.AgeCheck.Under13);
        var back = await Protobot.Online.Accounts.SignIn(user, pass);
        sb.AppendLine("sign in: " + (back ?? "ok") + ", birth " + Protobot.Online.Accounts.Birth + ", under 13 " + Protobot.Online.AgeCheck.Under13);
        Mark();
        var deleted = await Protobot.Online.Accounts.DeleteEverything();
        sb.AppendLine("deleted: " + (deleted ?? "ok") + ", guest " + Protobot.Online.Accounts.IsGuest + ", under 13 " + Protobot.Online.AgeCheck.Under13);
        auth.SignOut(true);
        auth.SwitchProfile("default");
        await auth.SignInAnonymouslyAsync();
        await Protobot.Online.Accounts.LoadBirth();
        sb.AppendLine("back to the editor's player: " + (auth.PlayerId == editorPlayer) + " (" + Protobot.Online.Accounts.Username + "), its birth '" + Protobot.Online.Accounts.Birth + "'");
    }
    catch (System.Exception e) { sb.AppendLine("error " + e.GetType().Name + ": " + e.Message); }
    System.IO.File.WriteAllText(outPath, sb.ToString());
};
run();
return "started";
