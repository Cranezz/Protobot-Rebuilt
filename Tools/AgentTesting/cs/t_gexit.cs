var g = Protobot.Game.SkillsGame.Current;
if (g != null) g.Exit();
return "exited; online window " + Protobot.Online.OnlineWindow.IsOpen + " match " + Protobot.Online.OnlineMatch.Active + " netparts " + UnityEngine.Object.FindObjectsByType<Protobot.Online.NetPart>(UnityEngine.FindObjectsSortMode.None).Length + " parts " + Protobot.PartsManager.FindLoadedObjects().Count;
