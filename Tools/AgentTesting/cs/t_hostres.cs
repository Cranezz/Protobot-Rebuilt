var t = System.AppDomain.CurrentDomain.GetData("hostTask") as System.Threading.Tasks.Task<string>;
if (t == null) return "no task";
if (!t.IsCompleted) return "still running";
var s = Protobot.Online.MatchSessions.Current;
return "result: " + (t.Result ?? "OK") + (s != null ? " | session " + s.Name + " code " + s.Code + " players " + s.PlayerCount + " | net host " + Unity.Netcode.NetworkManager.Singleton.IsHost : "");
