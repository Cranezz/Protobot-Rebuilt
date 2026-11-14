var d = Protobot.Builds.BuildFile.Read(System.IO.Path.Combine(Application.persistentDataPath, "Recovery/recovery.pbb"));
var g = UnityEngine.Object.FindAnyObjectByType<Protobot.Builds.CrashGuard>();
typeof(Protobot.Builds.CrashGuard).GetMethod("OfferAfterCrash", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(g, null);
return "recovery parts " + d.parts.Length;
