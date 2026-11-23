// (Play) Agrees to the current terms here (the developer's editor), sends a heartbeat now and reads the answer.
UnityEngine.PlayerPrefs.SetInt("V2.TermsAgreed", Protobot.Online.AgeCheck.TermsVersion);
var p = UnityEngine.Object.FindFirstObjectByType<Protobot.Online.Presence>();
if (p == null) return "no presence";
var mode = System.IO.File.Exists("Temp/presence.txt") ? System.IO.File.ReadAllText("Temp/presence.txt").Trim() : "read";
if (mode == "beat") { typeof(Protobot.Online.Presence).GetMethod("Beat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(p, null); return "sent"; }
return "online " + Protobot.Online.Presence.Online + " area " + Protobot.Online.Presence.Area + " ready " + Protobot.Online.AgeCheck.Ready;
