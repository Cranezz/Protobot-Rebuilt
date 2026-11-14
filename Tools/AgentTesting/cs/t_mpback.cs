var s = UnityEngine.Object.FindFirstObjectByType<Protobot.Online.OnlineScreen>();
s.GetType().GetMethod("BackToMenu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(s, null);
var t = System.Type.GetType("Protobot.Editor.Playtest, Protobot.Editor");
t.GetMethod("Frames").Invoke(null, new object[] { 3 });
var splash = System.Linq.Enumerable.FirstOrDefault(UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Transform>(), x => x.name == "Splash Screen" && x.gameObject.scene.IsValid());
return "screen open=" + Protobot.Online.OnlineWindow.IsOpen + " splash active=" + (splash != null && splash.gameObject.activeInHierarchy) + " mask=" + Protobot.PivotCamera.Main.camera.cullingMask;
