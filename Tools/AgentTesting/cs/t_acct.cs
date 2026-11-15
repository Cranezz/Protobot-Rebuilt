Protobot.Online.OnlineWindow.Open();
var w = UnityEngine.Object.FindFirstObjectByType<Protobot.Online.OnlineScreen>();
w.Show(Protobot.Online.OnlineScreen.Page.PAGE);
return "shown";
