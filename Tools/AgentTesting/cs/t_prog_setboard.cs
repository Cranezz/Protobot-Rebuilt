var screen = UnityEngine.Object.FindAnyObjectByType<Protobot.Online.OnlineScreen>();
var t = screen.GetType();
var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
t.GetField("boardProgramming", bf).SetValue(screen, true);
t.GetField("board", bf).SetValue(screen, null);
t.GetMethod("Rebuild", bf).Invoke(screen, null);
return "programming board";
