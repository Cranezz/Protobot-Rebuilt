// (Play) Badges: Temp/badge.txt "earn" = marks the beta badge earned here, shows the award card and claims it for the
// signed-in player; "read" = the player's badges, the counter and who's signed in.
var cmd = System.IO.File.Exists("Temp/badge.txt") ? System.IO.File.ReadAllText("Temp/badge.txt").Trim() : "read";
if (cmd == "earn") {
    UnityEngine.PlayerPrefs.SetFloat("V2.Badges.BetaSeconds", 600f);
    UnityEngine.PlayerPrefs.SetInt("V2.Badges.BetaEarned", 1);
    Protobot.Online.Badges.ShowAward(Protobot.Online.Badges.Beta);
    _ = Protobot.Online.Badges.Claim();
    return "earned, claiming";
}
return "badges [" + Protobot.Online.Badges.Text + "] played " + Protobot.Online.Badges.BetaPlayed + " earned " + Protobot.Online.Badges.BetaEarnedHere
    + " ready " + Protobot.Online.AgeCheck.Ready + " signed in " + Protobot.Online.OnlineServices.SignedIn + " guest " + Protobot.Online.Accounts.IsGuest + " name " + Protobot.Online.OnlineServices.Name;
