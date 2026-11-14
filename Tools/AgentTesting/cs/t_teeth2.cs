var b = GameObject.Find("Chamfer"); if (b == null) return "no button";
b.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
typeof(Protobot.Tools.PartSaw).GetField("teethMode", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).SetValue(null, true);
Protobot.Interface.ToolOptions.Rebuild();
return "ok";
