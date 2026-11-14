var t = System.Type.GetType("Protobot.Online.OnlineScreen, Assembly-CSharp");
return (t != null ? "compiled" : "missing") + " compiling=" + UnityEditor.EditorApplication.isCompiling;
