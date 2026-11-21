var s = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
return "dirty " + s.isDirty + " parts " + UnityEngine.Object.FindObjectsByType<Protobot.SavedObject>(UnityEngine.FindObjectsSortMode.None).Length;
