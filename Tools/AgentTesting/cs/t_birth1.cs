// (Play) V2 birthdays, part 1: the age math, then a throwaway guest (own profile: the editor's player, the user's
// account, is untouched) left signed in for screenshots of the Account page. Temp/birth1.txt.
var outPath = System.IO.Path.GetFullPath("Temp/birth1.txt");
System.IO.File.WriteAllText(outPath, "working");
var sb = new System.Text.StringBuilder();
var y = System.DateTime.Now.Year; var m = System.DateTime.Now.Month;
var lastMonth = m == 1 ? 12 : m - 1;
var lastMonthYear = m == 1 ? y - 1 : y;
sb.AppendLine("13 last month: " + Protobot.Online.AgeCheck.Age((lastMonthYear - 13).ToString("0000") + "-" + lastMonth.ToString("00"))
              + ", 13 this month (not yet): " + Protobot.Online.AgeCheck.Age((y - 13).ToString("0000") + "-" + m.ToString("00")));
System.Func<System.Threading.Tasks.Task> run = async () => {
    try {
        await Protobot.Online.OnlineServices.Start();
        var auth = Unity.Services.Authentication.AuthenticationService.Instance;
        System.IO.File.WriteAllText(System.IO.Path.GetFullPath("Temp/birth_editor.txt"), auth.PlayerId);
        auth.SignOut();
        auth.SwitchProfile("tbirth" + new System.Random().Next(1000, 9999));
        await auth.SignInAnonymouslyAsync();
        sb.AppendLine("throwaway guest, guest " + Protobot.Online.Accounts.IsGuest + ", account birth " + (Protobot.Online.Accounts.Birth ?? "null"));
    }
    catch (System.Exception e) { sb.AppendLine("error " + e.Message); }
    System.IO.File.WriteAllText(outPath, sb.ToString());
};
run();
return "started";
