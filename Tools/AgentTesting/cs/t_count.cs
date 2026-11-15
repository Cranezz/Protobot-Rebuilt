return "parts " + Protobot.PartsManager.FindLoadedObjects().Count + ", playing " + UnityEditor.EditorApplication.isPlaying + ", game " + (Protobot.Game.SkillsGame.Current != null);
