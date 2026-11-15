var v = UnityEngine.Object.FindAnyObjectByType<Protobot.Game.ReplayViewer>();
if (v != null) v.Close();
var screen = UnityEngine.Object.FindAnyObjectByType<Protobot.Online.OnlineScreen>();
if (screen != null) UnityEngine.Object.Destroy(screen.gameObject);
return "closed";
