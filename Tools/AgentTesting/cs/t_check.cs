var t = System.Type.GetType("Protobot.Game.SkillsGame, Assembly-CSharp");
return (t.GetField("skipAutonomous") != null ? "compiled" : "old") + " compiling=" + UnityEditor.EditorApplication.isCompiling;
